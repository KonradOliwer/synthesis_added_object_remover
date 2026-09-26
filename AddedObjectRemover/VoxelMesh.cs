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
    /// <summary>Upper bound for the distance from any triangle point to its nearest sample, in units of Step: sqrt(1/2), rounded up.</summary>
    public const float SamplingErrorFactor = 0.7072f;

    /// <summary>
    /// Hard cap on samples per mesh: a mesh that would exceed it is sampled more coarsely (larger
    /// Step), and a mesh that exceeds it even at one sample per triangle corner is not voxelized.
    /// </summary>
    public const long MaxSamples = 2_000_000;

    /// <summary>Half the space diagonal of a unit cube, sqrt(3)/2, rounded up.</summary>
    private const float HalfCubeDiagonalFactor = 0.8661f;

    private const float MinEdgeLength = 1e-4f;
    private const int MaxCoarseningSteps = 8;
    private const float CoarseningFactor = 1.5f;

    /// <summary>Keeps the coarsest step strictly above every triangle's own extent despite rounding.</summary>
    private const float CoarsestStepMargin = 1.01f;

    /// <summary>Voxel keys pack three signed voxel coordinates into one long, this many bits each.</summary>
    private const int BitsPerAxis = 21;
    private const int KeyOffset = 1 << (BitsPerAxis - 1);
    private const long AxisMask = (1L << BitsPerAxis) - 1;

    private readonly NifGeometry _geometry;
    private readonly Dictionary<long, int> _slotByKey;
    private readonly int[] _slotCoords;   // 3 ints (voxel x, y, z) per slot
    private readonly int[] _sampleStart;  // slot -> first sample; length = slots + 1
    private readonly Vector3[] _samples;
    private readonly int[] _triangleStart; // slot -> first entry in _triangles; length = slots + 1
    private readonly int[] _triangles;

    private VoxelMesh(
        NifGeometry geometry,
        float voxelSize,
        float step,
        bool coarsened,
        SampleSlots slots,
        Vector3[] samples,
        TriangleIndex triangles)
    {
        _geometry = geometry;
        VoxelSize = voxelSize;
        Step = step;
        Coarsened = coarsened;
        _slotByKey = slots.SlotByKey;
        _slotCoords = slots.SlotCoords;
        _sampleStart = slots.SampleStart;
        SampleBounds = slots.SampleBounds;
        _samples = samples;
        _triangleStart = triangles.Start;
        _triangles = triangles.Triangles;
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
        _geometry.Vertices.Length * 12L + _geometry.Indices.Length * 4L + _samples.Length * 12L + _triangles.Length * 4L
        + VoxelCount * (12L + 4L + 4L + 24L);

    /// <summary>AABB of all samples (the mesh's triangle bounds).</summary>
    public Box SampleBounds { get; }

    /// <summary>
    /// Voxelizes a mesh, or returns null when it has no triangles or cannot be sampled within
    /// <see cref="MaxSamples"/> even at its coarsest sampling.
    /// </summary>
    public static VoxelMesh? Build(NifGeometry geometry, float voxelSize)
    {
        if (geometry.TriangleCount == 0) return null;

        var baseStep = voxelSize * 0.5f;
        if (ChooseStep(geometry, baseStep, out var sampleCount) is not { } step) return null;

        var samples = SampleMesh(geometry, step, voxelSize, sampleCount);
        Array.Sort(samples.Keys, samples.Points);
        var slots = BuildSampleSlots(samples.Keys, samples.Points);
        var triangles = BuildTriangleIndex(samples.TriangleKeys, samples.TriangleIds, slots);

        return new VoxelMesh(geometry, voxelSize, step, coarsened: step > baseStep, slots, samples.Points, triangles);
    }

    /// <summary>Every sample with its voxel key, plus one (voxel key, triangle) entry per voxel a triangle has samples in.</summary>
    private sealed record MeshSamples(Vector3[] Points, long[] Keys, List<long> TriangleKeys, List<int> TriangleIds);

    private sealed record SampleSlots(Dictionary<long, int> SlotByKey, int[] SlotCoords, int[] SampleStart, Box SampleBounds)
    {
        public int Count => SampleStart.Length - 1;
    }

    private sealed record TriangleIndex(int[] Start, int[] Triangles);

    /// <remarks>Arrays are sized from the exact count, so the build never holds more than one copy of the samples.</remarks>
    private static MeshSamples SampleMesh(NifGeometry geometry, float step, float voxelSize, long sampleCount)
    {
        var points = new Vector3[sampleCount];
        var keys = new long[sampleCount];
        var written = 0;
        var triangleKeys = new List<long>();
        var triangleIds = new List<int>();
        var keysOfTriangle = new HashSet<long>();
        var triangleSamples = new List<Vector3>();
        var inverseVoxel = 1f / voxelSize;

        for (var t = 0; t < geometry.TriangleCount; t++)
        {
            triangleSamples.Clear();
            keysOfTriangle.Clear();
            var (a, b, c) = geometry.GetTriangle(t);
            SampleTriangle(a, b, c, step, triangleSamples);
            foreach (var sample in triangleSamples)
            {
                var key = KeyOf(sample, inverseVoxel);
                points[written] = sample;
                keys[written] = key;
                written++;
                if (keysOfTriangle.Add(key))
                {
                    triangleKeys.Add(key);
                    triangleIds.Add(t);
                }
            }
        }
        return new MeshSamples(points, keys, triangleKeys, triangleIds);
    }

    /// <summary>One slot per distinct key of the key-sorted samples, in key order.</summary>
    private static SampleSlots BuildSampleSlots(long[] sortedKeys, Vector3[] samples)
    {
        var slotByKey = new Dictionary<long, int>();
        var slotCoords = new List<int>();
        var sampleStart = new List<int>();
        var boundsMin = new Vector3(float.PositiveInfinity);
        var boundsMax = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < sortedKeys.Length; i++)
        {
            boundsMin = Vector3.Min(boundsMin, samples[i]);
            boundsMax = Vector3.Max(boundsMax, samples[i]);
            if (i > 0 && sortedKeys[i] == sortedKeys[i - 1]) continue;
            slotByKey[sortedKeys[i]] = sampleStart.Count;
            sampleStart.Add(i);
            var (x, y, z) = Unpack(sortedKeys[i]);
            slotCoords.Add(x);
            slotCoords.Add(y);
            slotCoords.Add(z);
        }
        var slotCount = sampleStart.Count;
        sampleStart.Add(sortedKeys.Length);

        var sampleBounds = slotCount == 0 ? Box.Zero : new Box(boundsMin, boundsMax);
        return new SampleSlots(slotByKey, slotCoords.ToArray(), sampleStart.ToArray(), sampleBounds);
    }

    private static TriangleIndex BuildTriangleIndex(List<long> triangleKeys, List<int> triangleIds, SampleSlots slots)
    {
        // Slots were assigned in sorted key order, so sorting by key groups the triangle ids by
        // slot in slot order.
        var keyArray = triangleKeys.ToArray();
        var idArray = triangleIds.ToArray();
        Array.Sort(keyArray, idArray);

        var start = new int[slots.Count + 1];
        foreach (var key in keyArray) start[slots.SlotByKey[key] + 1]++;
        for (var s = 0; s < slots.Count; s++) start[s + 1] += start[s];
        return new TriangleIndex(start, idArray);
    }

    /// <summary>
    /// Smallest tried sample step (starting at <paramref name="baseStep"/>) whose exact sample count
    /// is at most <see cref="MaxSamples"/>, or null when even one sample per triangle corner is too
    /// many. Growth is driven by the total triangle area (about 2 * area / step^2 samples); the
    /// last resort is a step no smaller than any triangle, which yields exactly the corner samples.
    /// </summary>
    private static float? ChooseStep(NifGeometry geometry, float baseStep, out long total)
    {
        total = CountSamples(geometry, baseStep);
        if (total <= MaxSamples) return baseStep;

        var coarsest = MeasureCoarsestSampling(geometry, baseStep);
        if (coarsest.CornerSamples > MaxSamples)
        {
            total = coarsest.CornerSamples;
            return null;
        }

        var step = baseStep;
        var budget = Math.Max(1, MaxSamples - coarsest.CornerSamples);
        for (var attempt = 0; attempt < MaxCoarseningSteps; attempt++)
        {
            var areaStep = (float)Math.Sqrt(2 * coarsest.Area / budget);
            step = MathF.Min(coarsest.Step, MathF.Max(step * CoarseningFactor, areaStep));
            total = CountSamples(geometry, step);
            if (total <= MaxSamples) return step;
            if (step >= coarsest.Step) break;
        }

        total = CountSamples(geometry, coarsest.Step);
        return total <= MaxSamples ? coarsest.Step : null;
    }

    /// <summary>
    /// Total triangle area, the sample count when every triangle yields only its corners, and a
    /// step large enough for that.
    /// </summary>
    private readonly record struct CoarsestSampling(double Area, long CornerSamples, float Step);

    private static CoarsestSampling MeasureCoarsestSampling(NifGeometry geometry, float baseStep)
    {
        double area = 0;
        long cornerSamples = 0;
        var step = baseStep;
        for (var t = 0; t < geometry.TriangleCount; t++)
        {
            var (a, b, c) = geometry.GetTriangle(t);
            var triangle = TriangleLayout.Of(a, b, c);
            if (triangle.IsDegenerate)
            {
                cornerSamples++;
                continue;
            }
            cornerSamples += 3;
            area += 0.5 * triangle.BaseLength * triangle.Height;
            step = MathF.Max(step, MathF.Max(2 * triangle.Height, triangle.BaseLength));
        }
        return new CoarsestSampling(area, cornerSamples, step * CoarsestStepMargin);
    }

    /// <summary>Exact sample count at <paramref name="step"/>; stops early once it exceeds <see cref="MaxSamples"/>.</summary>
    private static long CountSamples(NifGeometry geometry, float step)
    {
        long total = 0;
        for (var t = 0; t < geometry.TriangleCount && total <= MaxSamples; t++)
        {
            var (a, b, c) = geometry.GetTriangle(t);
            total += CountTriangleSamples(a, b, c, step);
        }
        return total;
    }

    /// <summary>
    /// True if the meshes of two placed references come within <paramref name="tolerance"/> world
    /// units of each other. Samples of <paramref name="sampledMesh"/> (normally the mesh with fewer
    /// voxels) are transformed to world space and then into <paramref name="lookupMesh"/>'s local
    /// frame, and their exact distance to the lookup mesh's triangles is tested. Because samples lie
    /// on the sampled surface, a positive result always means the real surfaces are at most
    /// tolerance apart; a gap smaller than tolerance - Step(sampled) * sqrt(1/2) * scale(sampled)
    /// is always found.
    /// </summary>
    public static bool Touches(
        VoxelMesh lookupMesh,
        PlacedTransform lookupTransform,
        VoxelMesh sampledMesh,
        PlacedTransform sampledTransform,
        float tolerance)
    {
        if (lookupMesh.VoxelCount == 0 || sampledMesh.VoxelCount == 0) return false;

        var toLookup = RelativeTransform.Create(lookupTransform, sampledTransform);
        var query = TouchQuery.Create(lookupMesh, sampledMesh, toLookup.Ratio, tolerance / lookupTransform.Scale);
        var candidateSlots = new List<int>();

        for (var sampledSlot = 0; sampledSlot < sampledMesh.VoxelCount; sampledSlot++)
        {
            var center = toLookup.Apply(sampledMesh.VoxelCenter(sampledSlot));
            if (!query.Reach.Contains(center)) continue;

            lookupMesh.CollectCandidateSlots(center, query.VoxelRadius, candidateSlots);
            if (candidateSlots.Count == 0) continue;

            if (lookupMesh.AnySampleWithinTolerance(sampledMesh, sampledSlot, toLookup, candidateSlots, query)) return true;
        }

        return false;
    }

    /// <summary>
    /// Sampled-local -> lookup-local: x_l = R_l^T * (pos_s + R_s * (s_s * x) - pos_l) / s_l,
    /// computed as Rotation * x * Ratio + Translation.
    /// </summary>
    private readonly record struct RelativeTransform(Mat3 Rotation, float Ratio, Vector3 Translation)
    {
        public static RelativeTransform Create(PlacedTransform lookup, PlacedTransform sampled) => new(
            lookup.Rotation.Transposed() * sampled.Rotation,
            sampled.Scale / lookup.Scale,
            lookup.Rotation.TransformTransposed(sampled.Position - lookup.Position) / lookup.Scale);

        public Vector3 Apply(Vector3 v) => Rotation.Transform(v) * Ratio + Translation;
    }

    /// <summary>Distances of one touch test, all in lookup-mesh units.</summary>
    private readonly record struct TouchQuery(float ToleranceSquared, float SearchRadiusSquared, float VoxelRadius, Box Reach)
    {
        public static TouchQuery Create(VoxelMesh lookupMesh, VoxelMesh sampledMesh, float ratio, float toleranceLocal)
        {
            // A triangle point within tolerance of a sampled point has a lookup sample of its own
            // (whose voxel lists the triangle) within this distance of the sampled point.
            var searchRadius = toleranceLocal + lookupMesh.Step * SamplingErrorFactor;
            // Any sampled point lies within this distance of its voxel's center.
            var sampledVoxelRadius = sampledMesh.VoxelSize * HalfCubeDiagonalFactor * ratio;
            var voxelRadius = searchRadius + sampledVoxelRadius;
            var reach = new Box(
                lookupMesh.SampleBounds.Min - new Vector3(voxelRadius),
                lookupMesh.SampleBounds.Max + new Vector3(voxelRadius));
            return new TouchQuery(toleranceLocal * toleranceLocal, searchRadius * searchRadius, voxelRadius, reach);
        }
    }

    /// <summary>Occupied voxels within <paramref name="radius"/> of <paramref name="center"/> (lookup-local).</summary>
    private void CollectCandidateSlots(Vector3 center, float radius, List<int> candidateSlots)
    {
        candidateSlots.Clear();
        var inverseVoxel = 1f / VoxelSize;
        var low = Floor((center - new Vector3(radius)) * inverseVoxel);
        var high = Floor((center + new Vector3(radius)) * inverseVoxel);
        var range = high - low + Vector3.One;
        if (range.X * range.Y * range.Z > VoxelCount)
        {
            // Range covers more voxels than the mesh has (e.g. the sampled mesh is scaled far larger).
            var radiusSquared = radius * radius;
            for (var slot = 0; slot < VoxelCount; slot++)
            {
                if (VoxelDistanceSquared(slot, center) <= radiusSquared) candidateSlots.Add(slot);
            }
            return;
        }

        for (var x = (int)low.X; x <= (int)high.X; x++)
        {
            for (var y = (int)low.Y; y <= (int)high.Y; y++)
            {
                for (var z = (int)low.Z; z <= (int)high.Z; z++)
                {
                    if (_slotByKey.TryGetValue(Pack(x, y, z), out var slot)) candidateSlots.Add(slot);
                }
            }
        }
    }

    private bool AnySampleWithinTolerance(
        VoxelMesh sampledMesh,
        int sampledSlot,
        RelativeTransform toLookup,
        List<int> candidateSlots,
        TouchQuery query)
    {
        for (var s = sampledMesh._sampleStart[sampledSlot]; s < sampledMesh._sampleStart[sampledSlot + 1]; s++)
        {
            var point = toLookup.Apply(sampledMesh._samples[s]);
            foreach (var slot in candidateSlots)
            {
                if (VoxelDistanceSquared(slot, point) > query.SearchRadiusSquared) continue;

                for (var i = _triangleStart[slot]; i < _triangleStart[slot + 1]; i++)
                {
                    var (a, b, c) = _geometry.GetTriangle(_triangles[i]);
                    if (Geometry.DistanceSquaredToTriangle(point, a, b, c) <= query.ToleranceSquared) return true;
                }
            }
        }
        return false;
    }

    private static Vector3 Floor(Vector3 v) => new(MathF.Floor(v.X), MathF.Floor(v.Y), MathF.Floor(v.Z));

    private Vector3 VoxelMin(int slot) =>
        new Vector3(_slotCoords[3 * slot], _slotCoords[3 * slot + 1], _slotCoords[3 * slot + 2]) * VoxelSize;

    private Vector3 VoxelCenter(int slot) =>
        new Vector3(
            _slotCoords[3 * slot] + 0.5f,
            _slotCoords[3 * slot + 1] + 0.5f,
            _slotCoords[3 * slot + 2] + 0.5f) * VoxelSize;

    private float VoxelDistanceSquared(int slot, Vector3 point)
    {
        var voxelMin = VoxelMin(slot);
        return Geometry.DistanceSquaredToBox(point, voxelMin, voxelMin + new Vector3(VoxelSize));
    }

    /// <summary>
    /// A triangle named so that Left-Right is its longest edge, with the Apex opposite it; both
    /// base angles are then acute.
    /// </summary>
    private readonly record struct TriangleLayout(Vector3 Apex, Vector3 Left, Vector3 Right, float BaseLength, float Height)
    {
        public bool IsDegenerate => !(BaseLength > MinEdgeLength);

        public static TriangleLayout Of(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 apex, left, right;
            var lab = Vector3.DistanceSquared(a, b);
            var lbc = Vector3.DistanceSquared(b, c);
            var lca = Vector3.DistanceSquared(c, a);
            if (lbc >= lab && lbc >= lca) { apex = a; left = b; right = c; }
            else if (lca >= lab) { apex = b; left = c; right = a; }
            else { apex = c; left = a; right = b; }

            var baseLength = Vector3.Distance(left, right);
            var height = Vector3.Cross(left - apex, right - apex).Length() / baseLength;
            return new TriangleLayout(apex, left, right, baseLength, height);
        }

        /// <summary>Rows at most step/2 apart, or null when that exceeds <see cref="MaxSamples"/>.</summary>
        public int? RowCount(float step)
        {
            var rowCount = Math.Ceiling(Height / (step * 0.5));
            if (!(rowCount <= MaxSamples)) return null;
            return Math.Max(1, (int)rowCount);
        }

        /// <summary>Intervals of at most step along row <paramref name="lambda"/> (0 at the apex), or null beyond <see cref="MaxSamples"/>.</summary>
        public int? PointIntervals(float lambda, float step)
        {
            var pointCount = Math.Ceiling(BaseLength * lambda / (double)step);
            if (!(pointCount <= MaxSamples)) return null;
            return (int)pointCount;
        }
    }

    /// <summary>
    /// Number of samples <see cref="SampleTriangle"/> produces, or MaxSamples + 1 as soon as it
    /// exceeds <see cref="MaxSamples"/>.
    /// </summary>
    private static long CountTriangleSamples(Vector3 a, Vector3 b, Vector3 c, float step)
    {
        var triangle = TriangleLayout.Of(a, b, c);
        if (triangle.IsDegenerate) return 1;
        if (triangle.RowCount(step) is not { } rows) return MaxSamples + 1;

        long count = 0;
        for (var k = 0; k <= rows; k++)
        {
            if (triangle.PointIntervals((float)k / rows, step) is not { } intervals) return MaxSamples + 1;
            if (intervals == 0)
            {
                count++;
                continue;
            }
            count += intervals + 1;
            if (count > MaxSamples) return MaxSamples + 1;
        }
        return count;
    }

    /// <summary>
    /// Samples one triangle: rows parallel to its longest edge, at most step/2 apart, each sampled
    /// at most step apart (endpoints included). Every triangle point is then within
    /// step * sqrt(1/2) of a sample.
    /// </summary>
    private static void SampleTriangle(Vector3 a, Vector3 b, Vector3 c, float step, List<Vector3> output)
    {
        var triangle = TriangleLayout.Of(a, b, c);
        if (triangle.IsDegenerate)
        {
            output.Add(triangle.Apex);
            return;
        }
        if (triangle.RowCount(step) is not { } rows) return;

        for (var k = 0; k <= rows; k++)
        {
            var lambda = (float)k / rows;
            var rowStart = triangle.Apex + (triangle.Left - triangle.Apex) * lambda;
            var rowEnd = triangle.Apex + (triangle.Right - triangle.Apex) * lambda;
            if (triangle.PointIntervals(lambda, step) is not { } intervals) return;
            if (intervals == 0)
            {
                output.Add(rowStart);
                continue;
            }
            for (var i = 0; i <= intervals; i++)
            {
                output.Add(Vector3.Lerp(rowStart, rowEnd, (float)i / intervals));
            }
        }
    }

    private static long KeyOf(Vector3 p, float inverseVoxel) =>
        Pack((int)MathF.Floor(p.X * inverseVoxel), (int)MathF.Floor(p.Y * inverseVoxel), (int)MathF.Floor(p.Z * inverseVoxel));

    private static long Pack(int x, int y, int z)
    {
        static long Field(int v) => Math.Clamp(v, -KeyOffset, KeyOffset - 1) + KeyOffset;
        return (Field(x) << (2 * BitsPerAxis)) | (Field(y) << BitsPerAxis) | Field(z);
    }

    private static (int X, int Y, int Z) Unpack(long key) => (
        (int)((key >> (2 * BitsPerAxis)) & AxisMask) - KeyOffset,
        (int)((key >> BitsPerAxis) & AxisMask) - KeyOffset,
        (int)(key & AxisMask) - KeyOffset);
}
