using System.Collections.Concurrent;

namespace AddedObjectRemover;

internal readonly record struct TriangleStoreStats(
    int Built,
    int Rebuilt,
    int TooLarge,
    int Evicted,
    long Triangles,
    long PeakResidentMeshes,
    long PeakResidentBytes);

/// <summary>One use of a stored tree; disposing it ends the use. A default lease holds nothing.</summary>
internal readonly struct TriangleLease : IDisposable
{
    private readonly TriangleStore? _store;
    private readonly TriangleStore.Entry? _entry;

    internal TriangleLease(TriangleStore store, TriangleStore.Entry entry, MeshTriangleTree? tree)
    {
        _store = store;
        _entry = entry;
        Tree = tree;
    }

    /// <summary>Null when the mesh has no usable triangles or is too large.</summary>
    public MeshTriangleTree? Tree { get; }

    public void Dispose()
    {
        if (_store != null && _entry != null) _store.EndUse(_entry);
    }
}

/// <summary>
/// Thread-safe store of <see cref="MeshTriangleTree"/> per mesh path. A mesh is built on its first
/// use and stays resident until the resident estimate exceeds the maximum resident bytes; then
/// the least recently used meshes that are not in use are dropped (and rebuilt if needed again).
/// Builds of large meshes are limited to a few at a time, because reading and indexing
/// temporarily needs several times the finished tree's memory.
/// </summary>
/// <param name="maxResidentBytes">The resident estimate above which idle trees are evicted, down to three quarters of it.</param>
internal sealed class TriangleStore(Func<string, NifGeometry?> readGeometry, long maxResidentBytes = TriangleStore.DefaultMaxResidentBytes)
{
    public const long DefaultMaxResidentBytes = 1L << 30;

    private readonly long _evictToBytes = maxResidentBytes / 4 * 3;
    private const int LargeMeshTriangles = 20_000;
    private const int MaxConcurrentLargeBuilds = 4;

    /// <summary><see cref="Users"/> and <see cref="LastUse"/> are guarded by the store lock.</summary>
    internal sealed class Entry(Lazy<MeshTriangleTree?> tree)
    {
        public Lazy<MeshTriangleTree?> Tree { get; } = tree;
        public long LastUse;
        public int Users;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _everBuilt = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _largeBuilds = new(MaxConcurrentLargeBuilds);
    private readonly object _lock = new();

    private long _clock;
    private long _residentBytes;
    private long _peakResidentBytes;
    private long _residentMeshes;
    private long _peakResidentMeshes;
    private int _built;
    private int _rebuilt;
    private int _tooLarge;
    private int _evicted;
    private long _triangles;

    /// <summary>
    /// The tree stays resident at least until the returned lease is disposed. When the build
    /// throws, the exception passes through and the mesh is built again on its next use.
    /// </summary>
    public TriangleLease Acquire(string meshPath)
    {
        var entry = BeginUse(meshPath);
        MeshTriangleTree? tree;
        try
        {
            tree = entry.Tree.Value;
        }
        catch
        {
            EndFailedUse(meshPath, entry);
            throw;
        }
        if (tree != null && Interlocked.Read(ref _residentBytes) > maxResidentBytes) EvictLeastRecentlyUsed();
        return new TriangleLease(this, entry, tree);
    }

    public TriangleStoreStats GetStats() => new(
        Volatile.Read(ref _built),
        Volatile.Read(ref _rebuilt),
        Volatile.Read(ref _tooLarge),
        Volatile.Read(ref _evicted),
        Interlocked.Read(ref _triangles),
        Interlocked.Read(ref _peakResidentMeshes),
        Interlocked.Read(ref _peakResidentBytes));

    internal void EndUse(Entry entry)
    {
        lock (_lock) entry.Users--;
    }

    private Entry BeginUse(string meshPath)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(meshPath, out var entry))
            {
                entry = new Entry(new Lazy<MeshTriangleTree?>(() => Build(meshPath), LazyThreadSafetyMode.ExecutionAndPublication));
                _entries[meshPath] = entry;
            }
            entry.Users++;
            entry.LastUse = ++_clock;
            return entry;
        }
    }

    /// <summary>A lazy value keeps its exception, so the failed entry is dropped (unless another use already replaced it).</summary>
    private void EndFailedUse(string meshPath, Entry entry)
    {
        lock (_lock)
        {
            entry.Users--;
            if (_entries.TryGetValue(meshPath, out var current) && current == entry) _entries.Remove(meshPath);
        }
    }

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
        geometry = readGeometry(meshPath);
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

    private void EvictLeastRecentlyUsed()
    {
        lock (_lock)
        {
            if (Interlocked.Read(ref _residentBytes) <= maxResidentBytes) return;
            var idle = _entries
                .Where(kv => kv.Value.Users == 0 && kv.Value.Tree is { IsValueCreated: true, Value: not null })
                .OrderBy(kv => kv.Value.LastUse)
                .ToList();
            foreach (var (meshPath, entry) in idle)
            {
                if (Interlocked.Read(ref _residentBytes) <= _evictToBytes) break;
                _entries.Remove(meshPath);
                Interlocked.Add(ref _residentBytes, -entry.Tree.Value!.EstimatedBytes);
                Interlocked.Decrement(ref _residentMeshes);
                _evicted++;
            }
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
}
