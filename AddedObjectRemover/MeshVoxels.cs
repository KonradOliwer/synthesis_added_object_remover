using System.Collections.Concurrent;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Surface voxelization of one mesh (in mesh-local = NIF root space) used for the exact touch test.
///
/// Every render triangle is sampled with points spaced at most <see cref="Step"/> apart along
/// rows at most Step/2 apart, so every point of the triangle lies within Step * sqrt(1/2) of a
/// sample. Samples are bucketed into voxels of edge <see cref="VoxelSize"/>; each voxel stores its
/// samples and the triangles that have a sample in it. The voxels are only an index: the touch
/// decision itself is an exact point-to-triangle distance (see <see cref="Touches"/>).
/// </summary>
internal sealed class VoxelMesh
{
    /// <summary>Upper bound for the distance from any triangle point to its nearest sample, in units of Step.</summary>
    public const float SamplingErrorFactor = 0.7072f;

    /// <summary>
    /// Hard cap on samples per mesh: a mesh that would exceed it is sampled more coarsely (larger
    /// Step), and a mesh that exceeds it even at one sample per triangle corner is not voxelized.
    /// </summary>
    public const long MaxSamples = 2_000_000;

    private const float MinEdgeLength = 1e-4f;
    private const int MaxCoarseningSteps = 8;
    private const float CoarseningFactor = 1.5f;

    private const int KeyOffset = 1 << 20;

    private readonly Vector3[] _vertices;
    private readonly int[] _indices;
    private readonly Dictionary<long, int> _slotByKey;
    private readonly int[] _slotCoords;   // 3 ints (voxel x, y, z) per slot
    private readonly int[] _sampleStart;  // slot -> first sample; length = slots + 1
    private readonly Vector3[] _samples;
    private readonly int[] _triangleStart; // slot -> first entry in _triangles; length = slots + 1
    private readonly int[] _triangles;

    private VoxelMesh(
        float voxelSize,
        float step,
        Vector3[] vertices,
        int[] indices,
        Dictionary<long, int> slotByKey,
        int[] slotCoords,
        int[] sampleStart,
        Vector3[] samples,
        int[] triangleStart,
        int[] triangles,
        Box sampleBounds,
        bool coarsened)
    {
        VoxelSize = voxelSize;
        Step = step;
        Coarsened = coarsened;
        _vertices = vertices;
        _indices = indices;
        _slotByKey = slotByKey;
        _slotCoords = slotCoords;
        _sampleStart = sampleStart;
        _samples = samples;
        _triangleStart = triangleStart;
        _triangles = triangles;
        SampleBounds = sampleBounds;
    }

    public float VoxelSize { get; }

    /// <summary>Sample spacing actually used (VoxelSize / 2, larger only for huge meshes).</summary>
    public float Step { get; }

    public int VoxelCount => _slotCoords.Length / 3;

    public int SampleCount => _samples.Length;

    /// <summary>True when the mesh was sampled more coarsely than VoxelSize / 2 to stay within <see cref="MaxSamples"/>.</summary>
    public bool Coarsened { get; }

    /// <summary>Approximate managed memory held by this mesh, in bytes.</summary>
    public long EstimatedBytes =>
        _vertices.Length * 12L + _indices.Length * 4L + _samples.Length * 12L + _triangles.Length * 4L
        + VoxelCount * (12L + 4L + 4L + 24L);

    /// <summary>AABB of all samples (the mesh's triangle bounds).</summary>
    public Box SampleBounds { get; }

    /// <summary>
    /// Voxelizes a mesh, or returns null when it has no triangles or cannot be sampled within
    /// <see cref="MaxSamples"/> even at its coarsest sampling.
    /// </summary>
    public static VoxelMesh? Build(NifGeometry geometry, float voxelSize)
    {
        var vertices = geometry.Vertices;
        var indices = geometry.Indices;
        var triangleCount = indices.Length / 3;
        if (triangleCount == 0) return null;

        var baseStep = voxelSize * 0.5f;
        if (ChooseStep(vertices, indices, triangleCount, baseStep, out var total) is not { } step) return null;

        // Arrays are sized from the exact count, so the build never holds more than one copy of the samples.
        var sampleArray = new Vector3[total];
        var sampleKeyArray = new long[total];
        var sampleCount = 0;
        var triangleKeys = new List<long>();
        var triangleIds = new List<int>();
        var keysOfTriangle = new HashSet<long>();
        var triangleSamples = new List<Vector3>();
        var inverseVoxel = 1f / voxelSize;

        for (var t = 0; t < triangleCount; t++)
        {
            triangleSamples.Clear();
            keysOfTriangle.Clear();
            SampleTriangle(vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]], step, triangleSamples);
            foreach (var sample in triangleSamples)
            {
                var key = KeyOf(sample, inverseVoxel);
                sampleArray[sampleCount] = sample;
                sampleKeyArray[sampleCount] = key;
                sampleCount++;
                if (keysOfTriangle.Add(key))
                {
                    triangleKeys.Add(key);
                    triangleIds.Add(t);
                }
            }
        }

        Array.Sort(sampleKeyArray, sampleArray);

        var slotByKey = new Dictionary<long, int>();
        var slotCoords = new List<int>();
        var sampleStart = new List<int>();
        var boundsMin = new Vector3(float.PositiveInfinity);
        var boundsMax = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < sampleKeyArray.Length; i++)
        {
            boundsMin = Vector3.Min(boundsMin, sampleArray[i]);
            boundsMax = Vector3.Max(boundsMax, sampleArray[i]);
            if (i > 0 && sampleKeyArray[i] == sampleKeyArray[i - 1]) continue;
            slotByKey[sampleKeyArray[i]] = sampleStart.Count;
            sampleStart.Add(i);
            var (x, y, z) = Unpack(sampleKeyArray[i]);
            slotCoords.Add(x);
            slotCoords.Add(y);
            slotCoords.Add(z);
        }
        var slotCount = sampleStart.Count;
        sampleStart.Add(sampleKeyArray.Length);

        var triangleKeyArray = triangleKeys.ToArray();
        var triangleIdArray = triangleIds.ToArray();
        Array.Sort(triangleKeyArray, triangleIdArray);
        var triangleStart = new int[slotCount + 1];
        foreach (var key in triangleKeyArray) triangleStart[slotByKey[key] + 1]++;
        for (var s = 0; s < slotCount; s++) triangleStart[s + 1] += triangleStart[s];
        // Keys are sorted, and slots were assigned in sorted key order, so the sorted triangle ids
        // are already grouped by slot in slot order.

        var sampleBounds = slotCount == 0 ? Box.Zero : new Box(boundsMin, boundsMax);
        return new VoxelMesh(
            voxelSize,
            step,
            vertices,
            indices,
            slotByKey,
            slotCoords.ToArray(),
            sampleStart.ToArray(),
            sampleArray,
            triangleStart,
            triangleIdArray,
            sampleBounds,
            coarsened: step > baseStep);
    }

    /// <summary>
    /// Smallest tried sample step (starting at <paramref name="baseStep"/>) whose exact sample count
    /// is at most <see cref="MaxSamples"/>, or null when even one sample per triangle corner is too
    /// many. Growth is driven by the total triangle area (about 2 * area / step^2 samples); the
    /// last resort is a step no smaller than any triangle, which yields exactly the corner samples.
    /// </summary>
    private static float? ChooseStep(Vector3[] vertices, int[] indices, int triangleCount, float baseStep, out long total)
    {
        total = CountSamples(vertices, indices, triangleCount, baseStep);
        if (total <= MaxSamples) return baseStep;

        double area = 0;
        long floor = 0;
        var floorStep = baseStep;
        for (var t = 0; t < triangleCount; t++)
        {
            OrderByLongestEdge(vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]], out var apex, out var left, out var right);
            var baseLength = Vector3.Distance(left, right);
            if (!(baseLength > MinEdgeLength))
            {
                floor++;
                continue;
            }
            var height = Vector3.Cross(left - apex, right - apex).Length() / baseLength;
            floor += 3;
            area += 0.5 * baseLength * height;
            floorStep = MathF.Max(floorStep, MathF.Max(2 * height, baseLength));
        }
        if (floor > MaxSamples)
        {
            total = floor;
            return null;
        }
        floorStep *= 1.01f;

        var step = baseStep;
        var budget = Math.Max(1, MaxSamples - floor);
        for (var attempt = 0; attempt < MaxCoarseningSteps; attempt++)
        {
            var areaStep = (float)Math.Sqrt(2 * area / budget);
            step = MathF.Min(floorStep, MathF.Max(step * CoarseningFactor, areaStep));
            total = CountSamples(vertices, indices, triangleCount, step);
            if (total <= MaxSamples) return step;
            if (step >= floorStep) break;
        }

        total = CountSamples(vertices, indices, triangleCount, floorStep);
        return total <= MaxSamples ? floorStep : null;
    }

    /// <summary>Exact sample count at <paramref name="step"/>; stops early once it exceeds <see cref="MaxSamples"/>.</summary>
    private static long CountSamples(Vector3[] vertices, int[] indices, int triangleCount, float step)
    {
        long total = 0;
        for (var t = 0; t < triangleCount && total <= MaxSamples; t++)
        {
            total += SampleTriangle(vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]], step, null);
        }
        return total;
    }

    /// <summary>
    /// True if the meshes of two placed references come within <paramref name="tolerance"/> world
    /// units of each other. Samples of <paramref name="q"/> (normally the mesh with fewer voxels)
    /// are transformed to world space and then into <paramref name="p"/>'s local frame, and their
    /// exact distance to <paramref name="p"/>'s triangles is tested. Because samples lie on q's
    /// surface, a positive result always means the real surfaces are at most tolerance apart; a
    /// gap smaller than tolerance - Step(q) * sqrt(1/2) * scale(q) is always found.
    /// </summary>
    public static bool Touches(VoxelMesh p, PlacedTransform pTransform, VoxelMesh q, PlacedTransform qTransform, float tolerance)
    {
        if (p.VoxelCount == 0 || q.VoxelCount == 0) return false;

        // q-local -> p-local: x_p = R_p^T * (pos_q + R_q * (s_q * x) - pos_p) / s_p.
        var rotation = pTransform.Rotation.Transposed() * qTransform.Rotation;
        var ratio = qTransform.Scale / pTransform.Scale;
        var translation = pTransform.Rotation.TransformTransposed(qTransform.Position - pTransform.Position) / pTransform.Scale;

        var toleranceLocal = tolerance / pTransform.Scale;
        var toleranceSquared = toleranceLocal * toleranceLocal;
        // A triangle point within tolerance of a q sample has a sample of its own (whose voxel
        // lists the triangle) within this distance of the q sample.
        var searchRadius = toleranceLocal + p.Step * SamplingErrorFactor;
        var searchRadiusSquared = searchRadius * searchRadius;
        // Any q sample lies within this distance (in p units) of its voxel's center.
        var qVoxelRadius = q.VoxelSize * 0.8661f * ratio;
        var voxelRadius = searchRadius + qVoxelRadius;
        var reach = new Box(p.SampleBounds.Min - new Vector3(voxelRadius), p.SampleBounds.Max + new Vector3(voxelRadius));

        var candidateSlots = new List<int>();
        var pInverseVoxel = 1f / p.VoxelSize;

        for (var qSlot = 0; qSlot < q.VoxelCount; qSlot++)
        {
            var qCenter = new Vector3(
                q._slotCoords[3 * qSlot] + 0.5f,
                q._slotCoords[3 * qSlot + 1] + 0.5f,
                q._slotCoords[3 * qSlot + 2] + 0.5f) * q.VoxelSize;
            var center = rotation.Transform(qCenter) * ratio + translation;
            if (!reach.Contains(center)) continue;

            // Occupied p voxels near this q voxel.
            candidateSlots.Clear();
            var low = Floor((center - new Vector3(voxelRadius)) * pInverseVoxel);
            var high = Floor((center + new Vector3(voxelRadius)) * pInverseVoxel);
            var range = high - low + Vector3.One;
            if (range.X * range.Y * range.Z > p.VoxelCount)
            {
                // Range covers more voxels than p has (e.g. q is scaled far larger): test p's voxels directly.
                var radiusSquared = voxelRadius * voxelRadius;
                for (var pSlot = 0; pSlot < p.VoxelCount; pSlot++)
                {
                    var voxelMin = p.VoxelMin(pSlot);
                    if (Geometry.DistanceSquaredToBox(center, voxelMin, voxelMin + new Vector3(p.VoxelSize)) <= radiusSquared)
                    {
                        candidateSlots.Add(pSlot);
                    }
                }
            }
            else
            {
                for (var x = (int)low.X; x <= (int)high.X; x++)
                {
                    for (var y = (int)low.Y; y <= (int)high.Y; y++)
                    {
                        for (var z = (int)low.Z; z <= (int)high.Z; z++)
                        {
                            if (p._slotByKey.TryGetValue(Pack(x, y, z), out var pSlot)) candidateSlots.Add(pSlot);
                        }
                    }
                }
            }
            if (candidateSlots.Count == 0) continue;

            for (var s = q._sampleStart[qSlot]; s < q._sampleStart[qSlot + 1]; s++)
            {
                var point = rotation.Transform(q._samples[s]) * ratio + translation;
                foreach (var pSlot in candidateSlots)
                {
                    var voxelMin = p.VoxelMin(pSlot);
                    if (Geometry.DistanceSquaredToBox(point, voxelMin, voxelMin + new Vector3(p.VoxelSize)) > searchRadiusSquared) continue;

                    for (var i = p._triangleStart[pSlot]; i < p._triangleStart[pSlot + 1]; i++)
                    {
                        var t = p._triangles[i];
                        var distanceSquared = Geometry.DistanceSquaredToTriangle(
                            point,
                            p._vertices[p._indices[3 * t]],
                            p._vertices[p._indices[3 * t + 1]],
                            p._vertices[p._indices[3 * t + 2]]);
                        if (distanceSquared <= toleranceSquared) return true;
                    }
                }
            }
        }

        return false;
    }

    private static Vector3 Floor(Vector3 v) => new(MathF.Floor(v.X), MathF.Floor(v.Y), MathF.Floor(v.Z));

    private Vector3 VoxelMin(int slot) =>
        new Vector3(_slotCoords[3 * slot], _slotCoords[3 * slot + 1], _slotCoords[3 * slot + 2]) * VoxelSize;

    /// <summary>
    /// Samples one triangle: rows parallel to its longest edge, at most step/2 apart, each sampled
    /// at most step apart (endpoints included). Every triangle point is then within
    /// step * sqrt(1/2) of a sample. Returns the sample count and adds samples to
    /// <paramref name="output"/> when given; without output, returns MaxSamples + 1 as soon as the
    /// count exceeds <see cref="MaxSamples"/>.
    /// </summary>
    private static long SampleTriangle(Vector3 a, Vector3 b, Vector3 c, float step, List<Vector3>? output)
    {
        OrderByLongestEdge(a, b, c, out var apex, out var left, out var right);

        var baseLength = Vector3.Distance(left, right);
        if (!(baseLength > MinEdgeLength))
        {
            output?.Add(apex);
            return 1;
        }

        var height = Vector3.Cross(left - apex, right - apex).Length() / baseLength;
        var rowCount = Math.Ceiling(height / (step * 0.5));
        if (!(rowCount <= MaxSamples)) return MaxSamples + 1;
        var rows = Math.Max(1, (int)rowCount);
        long count = 0;
        for (var k = 0; k <= rows; k++)
        {
            var lambda = (float)k / rows;
            var rowStart = apex + (left - apex) * lambda;
            var rowEnd = apex + (right - apex) * lambda;
            var pointCount = Math.Ceiling(baseLength * lambda / (double)step);
            if (!(pointCount <= MaxSamples)) return MaxSamples + 1;
            var points = (int)pointCount;
            if (points == 0)
            {
                output?.Add(rowStart);
                count++;
                continue;
            }
            count += points + 1;
            if (output == null)
            {
                if (count > MaxSamples) return MaxSamples + 1;
                continue;
            }
            for (var i = 0; i <= points; i++)
            {
                output.Add(Vector3.Lerp(rowStart, rowEnd, (float)i / points));
            }
        }
        return count;
    }

    /// <summary>Names the triangle's corners so that left-right is its longest edge.</summary>
    private static void OrderByLongestEdge(Vector3 a, Vector3 b, Vector3 c, out Vector3 apex, out Vector3 left, out Vector3 right)
    {
        var lab = Vector3.DistanceSquared(a, b);
        var lbc = Vector3.DistanceSquared(b, c);
        var lca = Vector3.DistanceSquared(c, a);
        if (lbc >= lab && lbc >= lca) { apex = a; left = b; right = c; }
        else if (lca >= lab) { apex = b; left = c; right = a; }
        else { apex = c; left = a; right = b; }
    }

    private static long KeyOf(Vector3 p, float inverseVoxel) =>
        Pack((int)MathF.Floor(p.X * inverseVoxel), (int)MathF.Floor(p.Y * inverseVoxel), (int)MathF.Floor(p.Z * inverseVoxel));

    private static long Pack(int x, int y, int z)
    {
        static long Field(int v) => Math.Clamp(v, -KeyOffset, KeyOffset - 1) + KeyOffset;
        return (Field(x) << 42) | (Field(y) << 21) | Field(z);
    }

    private static (int X, int Y, int Z) Unpack(long key) => (
        (int)((key >> 42) & 0x1FFFFF) - KeyOffset,
        (int)((key >> 21) & 0x1FFFFF) - KeyOffset,
        (int)(key & 0x1FFFFF) - KeyOffset);
}

/// <summary>Counters of a <see cref="VoxelCache"/> run.</summary>
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

    /// <summary>Voxel mesh of the mesh at <paramref name="meshPath"/>, or null when it has no usable triangles or is too large.</summary>
    public VoxelMesh? Get(string meshPath)
    {
        var entry = _meshes.GetOrAdd(
            meshPath,
            p => new Entry(new Lazy<VoxelMesh?>(() => Build(p), LazyThreadSafetyMode.ExecutionAndPublication)));
        Volatile.Write(ref entry.LastUse, Interlocked.Increment(ref _clock));
        var mesh = entry.Mesh.Value;
        if (mesh != null && Volatile.Read(ref _residentBytes) > MaxResidentBytes) Evict();
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

        var large = geometry.TriangleCount > LargeMeshTriangles;
        if (large) _largeBuilds.Wait();
        VoxelMesh? mesh;
        try
        {
            mesh = VoxelMesh.Build(geometry, _voxelSize);
        }
        finally
        {
            if (large) _largeBuilds.Release();
        }

        if (mesh == null)
        {
            Interlocked.Increment(ref _tooLarge);
            return null;
        }

        Interlocked.Increment(ref _built);
        if (!_everBuilt.TryAdd(meshPath, 0)) Interlocked.Increment(ref _rebuilt);
        if (mesh.Coarsened) Interlocked.Increment(ref _coarsened);
        Interlocked.Add(ref _voxels, mesh.VoxelCount);
        Interlocked.Add(ref _samples, mesh.SampleCount);
        UpdateMax(ref _peakResidentBytes, Interlocked.Add(ref _residentBytes, mesh.EstimatedBytes));
        UpdateMax(ref _peakResidentMeshes, Interlocked.Increment(ref _residentMeshes));
        return mesh;
    }

    /// <summary>Drops least recently used built meshes until the resident estimate is back under budget.</summary>
    private void Evict()
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
