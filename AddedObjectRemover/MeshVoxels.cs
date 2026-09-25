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

    /// <summary>A mesh whose sampling would exceed this many points is sampled more coarsely (larger Step).</summary>
    private const long MaxSamples = 2_000_000;

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
        Box sampleBounds)
    {
        VoxelSize = voxelSize;
        Step = step;
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

    /// <summary>AABB of all samples (the mesh's triangle bounds).</summary>
    public Box SampleBounds { get; }

    public static VoxelMesh Build(NifGeometry geometry, float voxelSize)
    {
        var vertices = geometry.Vertices;
        var indices = geometry.Indices;
        var triangleCount = indices.Length / 3;

        var step = voxelSize * 0.5f;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            long total = 0;
            for (var t = 0; t < triangleCount; t++)
            {
                total += SampleTriangle(vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]], step, null);
                if (total > MaxSamples) break;
            }
            if (total <= MaxSamples) break;
            step *= 1.5f;
        }

        var samples = new List<Vector3>();
        var sampleKeys = new List<long>();
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
                samples.Add(sample);
                sampleKeys.Add(key);
                if (keysOfTriangle.Add(key))
                {
                    triangleKeys.Add(key);
                    triangleIds.Add(t);
                }
            }
        }

        var sampleKeyArray = sampleKeys.ToArray();
        var sampleArray = samples.ToArray();
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
            sampleBounds);
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
    /// step * sqrt(1/2) of a sample. Returns the sample count; adds samples to
    /// <paramref name="output"/> when given.
    /// </summary>
    private static long SampleTriangle(Vector3 a, Vector3 b, Vector3 c, float step, List<Vector3>? output)
    {
        var lab = Vector3.DistanceSquared(a, b);
        var lbc = Vector3.DistanceSquared(b, c);
        var lca = Vector3.DistanceSquared(c, a);
        Vector3 apex, left, right;
        if (lbc >= lab && lbc >= lca) { apex = a; left = b; right = c; }
        else if (lca >= lab) { apex = b; left = c; right = a; }
        else { apex = c; left = a; right = b; }

        var baseLength = Vector3.Distance(left, right);
        if (!(baseLength > 1e-4f))
        {
            output?.Add(apex);
            return 1;
        }

        var height = Vector3.Cross(left - apex, right - apex).Length() / baseLength;
        var rows = Math.Max(1, (int)Math.Min(MathF.Ceiling(height / (step * 0.5f)), 1_000_000f));
        long count = 0;
        for (var k = 0; k <= rows; k++)
        {
            var lambda = (float)k / rows;
            var rowStart = apex + (left - apex) * lambda;
            var rowEnd = apex + (right - apex) * lambda;
            var points = (int)Math.Min(MathF.Ceiling(baseLength * lambda / step), 1_000_000f);
            if (points == 0)
            {
                output?.Add(rowStart);
                count++;
                continue;
            }
            count += points + 1;
            if (output == null) continue;
            for (var i = 0; i <= points; i++)
            {
                output.Add(Vector3.Lerp(rowStart, rowEnd, (float)i / points));
            }
        }
        return count;
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

/// <summary>
/// Thread-safe cache of <see cref="VoxelMesh"/> per mesh geometry (each mesh is voxelized once,
/// on first use).
/// </summary>
internal sealed class VoxelCache
{
    private readonly ConcurrentDictionary<NifGeometry, Lazy<VoxelMesh>> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly float _voxelSize;

    public VoxelCache(float voxelSize)
    {
        _voxelSize = voxelSize;
    }

    public int MeshCount => _meshes.Count;

    public VoxelMesh Get(NifGeometry geometry) =>
        _meshes.GetOrAdd(
            geometry,
            g => new Lazy<VoxelMesh>(() => VoxelMesh.Build(g, _voxelSize), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>Totals over all built meshes: voxels, samples, and meshes sampled more coarsely than VoxelSize / 2.</summary>
    public (long Voxels, long Samples, int Coarsened) GetTotals()
    {
        long voxels = 0, samples = 0;
        var coarsened = 0;
        foreach (var lazy in _meshes.Values)
        {
            if (!lazy.IsValueCreated) continue;
            var mesh = lazy.Value;
            voxels += mesh.VoxelCount;
            samples += mesh.SampleCount;
            if (mesh.Step > mesh.VoxelSize * 0.5f * 1.001f) coarsened++;
        }
        return (voxels, samples, coarsened);
    }
}
