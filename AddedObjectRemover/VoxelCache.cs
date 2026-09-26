using System.Collections.Concurrent;

namespace AddedObjectRemover;

internal readonly record struct VoxelCacheStats(
    int Built,
    int Rebuilt,
    int Coarsened,
    int TooLarge,
    int Evicted,
    long Voxels,
    long Samples,
    int PeakResidentMeshes,
    long PeakResidentBytes);

/// <summary>
/// Thread-safe, memory-bounded cache of <see cref="VoxelMesh"/> per mesh path. Triangles are read
/// on demand for the build and are then held only by the voxel mesh. When the estimated resident
/// size exceeds <see cref="MaxResidentBytes"/>, least recently used meshes are dropped (they are
/// rebuilt if needed again). Builds of large meshes are limited to a few at a time, because a
/// build temporarily needs several times the finished mesh's memory.
/// </summary>
internal sealed class VoxelCache
{
    private const long MaxResidentBytes = 1L << 30;
    private const long EvictToBytes = MaxResidentBytes / 4 * 3;
    private const int LargeMeshTriangles = 20_000;
    private const int MaxConcurrentLargeBuilds = 4;

    private sealed class Entry(Lazy<VoxelMesh?> mesh)
    {
        public Lazy<VoxelMesh?> Mesh { get; } = mesh;
        public long LastUse;
    }

    private readonly ConcurrentDictionary<string, Entry> _meshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _everBuilt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, NifGeometry?> _readGeometry;
    private readonly float _voxelSize;
    private readonly SemaphoreSlim _largeBuilds = new(MaxConcurrentLargeBuilds);
    private readonly object _evictLock = new();

    private long _clock;
    private long _residentBytes;
    private long _peakResidentBytes;
    private int _residentMeshes;
    private int _peakResidentMeshes;
    private int _built;
    private int _rebuilt;
    private int _coarsened;
    private int _tooLarge;
    private int _evicted;
    private long _voxels;
    private long _samples;

    public VoxelCache(float voxelSize, Func<string, NifGeometry?> readGeometry)
    {
        _voxelSize = voxelSize;
        _readGeometry = readGeometry;
    }

    /// <summary>Null when the mesh has no usable triangles or is too large.</summary>
    public VoxelMesh? Get(string meshPath)
    {
        var entry = _meshes.GetOrAdd(
            meshPath,
            p => new Entry(new Lazy<VoxelMesh?>(() => Build(p), LazyThreadSafetyMode.ExecutionAndPublication)));
        Volatile.Write(ref entry.LastUse, Interlocked.Increment(ref _clock));
        var mesh = entry.Mesh.Value;
        if (mesh != null && Volatile.Read(ref _residentBytes) > MaxResidentBytes) EvictLeastRecentlyUsed();
        return mesh;
    }

    public VoxelCacheStats GetStats() => new(
        Volatile.Read(ref _built),
        Volatile.Read(ref _rebuilt),
        Volatile.Read(ref _coarsened),
        Volatile.Read(ref _tooLarge),
        Volatile.Read(ref _evicted),
        Interlocked.Read(ref _voxels),
        Interlocked.Read(ref _samples),
        Volatile.Read(ref _peakResidentMeshes),
        Interlocked.Read(ref _peakResidentBytes));

    private VoxelMesh? Build(string meshPath)
    {
        var geometry = _readGeometry(meshPath);
        if (geometry is not { TriangleCount: > 0 }) return null;

        var mesh = BuildWithLargeMeshLimit(geometry);
        if (mesh == null)
        {
            Interlocked.Increment(ref _tooLarge);
            return null;
        }

        CountBuilt(meshPath, mesh);
        return mesh;
    }

    private VoxelMesh? BuildWithLargeMeshLimit(NifGeometry geometry)
    {
        var large = geometry.TriangleCount > LargeMeshTriangles;
        if (large) _largeBuilds.Wait();
        try
        {
            return VoxelMesh.Build(geometry, _voxelSize);
        }
        finally
        {
            if (large) _largeBuilds.Release();
        }
    }

    private void CountBuilt(string meshPath, VoxelMesh mesh)
    {
        Interlocked.Increment(ref _built);
        if (!_everBuilt.TryAdd(meshPath, 0)) Interlocked.Increment(ref _rebuilt);
        if (mesh.Coarsened) Interlocked.Increment(ref _coarsened);
        Interlocked.Add(ref _voxels, mesh.VoxelCount);
        Interlocked.Add(ref _samples, mesh.SampleCount);
        UpdateMax(ref _peakResidentBytes, Interlocked.Add(ref _residentBytes, mesh.EstimatedBytes));
        UpdateMax(ref _peakResidentMeshes, Interlocked.Increment(ref _residentMeshes));
    }

    /// <summary>Drops least recently used built meshes until the resident estimate is back under budget.</summary>
    private void EvictLeastRecentlyUsed()
    {
        if (!Monitor.TryEnter(_evictLock)) return;
        try
        {
            if (Volatile.Read(ref _residentBytes) <= MaxResidentBytes) return;
            var built = _meshes
                .Where(kv => kv.Value.Mesh.IsValueCreated && kv.Value.Mesh.Value != null)
                .OrderBy(kv => Volatile.Read(ref kv.Value.LastUse))
                .ToList();
            foreach (var pair in built)
            {
                if (Volatile.Read(ref _residentBytes) <= EvictToBytes) break;
                if (!_meshes.TryRemove(pair)) continue;
                Interlocked.Add(ref _residentBytes, -pair.Value.Mesh.Value!.EstimatedBytes);
                Interlocked.Decrement(ref _residentMeshes);
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
