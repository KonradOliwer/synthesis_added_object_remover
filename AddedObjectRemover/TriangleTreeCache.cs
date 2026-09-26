using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace AddedObjectRemover;

internal readonly record struct TriangleTreeStats(
    int Built,
    int Rebuilt,
    int TooLarge,
    int Evicted,
    long Triangles,
    int PeakResidentMeshes,
    long PeakResidentBytes);

/// <summary>
/// Thread-safe, reference-counted cache of <see cref="MeshTriangleTree"/> per mesh path. The
/// caller announces up front how many times each mesh will be acquired; a mesh is built on its
/// first acquisition and dropped as soon as its last user releases it, so each mesh is normally
/// built once and only meshes with pending work stay resident. If the resident estimate still
/// exceeds <see cref="MaxResidentBytes"/>, least recently used meshes are dropped as a fallback
/// (and rebuilt if needed again). Builds of large meshes are limited to a few at a time, because
/// reading and indexing temporarily needs several times the finished tree's memory.
/// </summary>
internal sealed class TriangleTreeCache
{
    private const long MaxResidentBytes = 1L << 30;
    private const long EvictToBytes = MaxResidentBytes / 4 * 3;
    private const int LargeMeshTriangles = 20_000;
    private const int MaxConcurrentLargeBuilds = 4;

    private sealed class Entry(Lazy<MeshTriangleTree?> tree)
    {
        public Lazy<MeshTriangleTree?> Tree { get; } = tree;
        public long LastUse;
    }

    private readonly ConcurrentDictionary<string, Entry> _trees = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _everBuilt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StrongBox<int>> _remainingUses;
    private readonly Func<string, NifGeometry?> _readGeometry;
    private readonly SemaphoreSlim _largeBuilds = new(MaxConcurrentLargeBuilds);
    private readonly object _evictLock = new();

    private long _clock;
    private long _residentBytes;
    private long _peakResidentBytes;
    private int _residentMeshes;
    private int _peakResidentMeshes;
    private int _built;
    private int _rebuilt;
    private int _tooLarge;
    private int _evicted;
    private long _triangles;

    /// <param name="usesByPath">How many times each mesh path will be acquired (and released).</param>
    public TriangleTreeCache(Func<string, NifGeometry?> readGeometry, IReadOnlyDictionary<string, int> usesByPath)
    {
        _readGeometry = readGeometry;
        _remainingUses = usesByPath.ToDictionary(
            kv => kv.Key,
            kv => new StrongBox<int>(kv.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Null when the mesh has no usable triangles or is too large. Every call must be matched by one <see cref="Release"/>.</summary>
    public MeshTriangleTree? Acquire(string meshPath)
    {
        var entry = _trees.GetOrAdd(
            meshPath,
            p => new Entry(new Lazy<MeshTriangleTree?>(() => Build(p), LazyThreadSafetyMode.ExecutionAndPublication)));
        Volatile.Write(ref entry.LastUse, Interlocked.Increment(ref _clock));
        var tree = entry.Tree.Value;
        if (tree != null && Volatile.Read(ref _residentBytes) > MaxResidentBytes) EvictLeastRecentlyUsed();
        return tree;
    }

    /// <summary>Counts one announced use as done; drops the mesh after its last use. Also valid for a use that never acquired the mesh.</summary>
    public void Release(string meshPath)
    {
        if (Interlocked.Decrement(ref _remainingUses[meshPath].Value) > 0) return;
        if (_trees.TryRemove(meshPath, out var entry)) Forget(entry);
    }

    public TriangleTreeStats GetStats() => new(
        Volatile.Read(ref _built),
        Volatile.Read(ref _rebuilt),
        Volatile.Read(ref _tooLarge),
        Volatile.Read(ref _evicted),
        Interlocked.Read(ref _triangles),
        Volatile.Read(ref _peakResidentMeshes),
        Interlocked.Read(ref _peakResidentBytes));

    private MeshTriangleTree? Build(string meshPath)
    {
        var tree = ReadAndIndexWithLargeMeshLimit(meshPath, out var geometry);
        if (geometry is not { TriangleCount: > 0 }) return null;
        if (tree == null)
        {
            Interlocked.Increment(ref _tooLarge);
            return null;
        }

        CountBuilt(meshPath, tree);
        return tree;
    }

    /// <remarks>The mesh size is only known after reading, so a large mesh takes a build slot once its triangles are in memory.</remarks>
    private MeshTriangleTree? ReadAndIndexWithLargeMeshLimit(string meshPath, out NifGeometry? geometry)
    {
        geometry = _readGeometry(meshPath);
        if (geometry is not { TriangleCount: > 0 }) return null;

        var large = geometry.TriangleCount > LargeMeshTriangles;
        if (large) _largeBuilds.Wait();
        try
        {
            return MeshTriangleTree.Build(geometry);
        }
        finally
        {
            if (large) _largeBuilds.Release();
        }
    }

    private void CountBuilt(string meshPath, MeshTriangleTree tree)
    {
        Interlocked.Increment(ref _built);
        if (!_everBuilt.TryAdd(meshPath, 0)) Interlocked.Increment(ref _rebuilt);
        Interlocked.Add(ref _triangles, tree.TriangleCount);
        UpdateMax(ref _peakResidentBytes, Interlocked.Add(ref _residentBytes, tree.EstimatedBytes));
        UpdateMax(ref _peakResidentMeshes, Interlocked.Increment(ref _residentMeshes));
    }

    /// <summary>Removes a dropped entry's tree from the resident estimate; entries still building or without a tree hold nothing.</summary>
    private void Forget(Entry entry)
    {
        if (!entry.Tree.IsValueCreated || entry.Tree.Value is not { } tree) return;
        Interlocked.Add(ref _residentBytes, -tree.EstimatedBytes);
        Interlocked.Decrement(ref _residentMeshes);
    }

    /// <summary>Fallback when the meshes with pending uses exceed the budget: drops least recently used built meshes until back under it.</summary>
    private void EvictLeastRecentlyUsed()
    {
        if (!Monitor.TryEnter(_evictLock)) return;
        try
        {
            if (Volatile.Read(ref _residentBytes) <= MaxResidentBytes) return;
            var built = _trees
                .Where(kv => kv.Value.Tree.IsValueCreated && kv.Value.Tree.Value != null)
                .OrderBy(kv => Volatile.Read(ref kv.Value.LastUse))
                .ToList();
            foreach (var pair in built)
            {
                if (Volatile.Read(ref _residentBytes) <= EvictToBytes) break;
                if (!_trees.TryRemove(pair)) continue;
                Forget(pair.Value);
                Interlocked.Increment(ref _evicted);
            }
        }
        finally
        {
            Monitor.Exit(_evictLock);
        }
    }

    private static void UpdateMax(ref long target, long value)
    {
        var current = Interlocked.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current) return;
            current = seen;
        }
    }

    private static void UpdateMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current) return;
            current = seen;
        }
    }
}
