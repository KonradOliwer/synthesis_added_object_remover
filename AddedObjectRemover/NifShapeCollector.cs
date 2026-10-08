using System.Numerics;
using NiflySharp;
using NiflySharp.Blocks;

namespace AddedObjectRemover;

/// <summary>Why shapes of one NIF were counted or skipped; printed when a NIF yields no geometry.</summary>
internal sealed class ShapeStats
{
    public int Shapes;
    public int Counted;
    public int Hidden;
    public int EditorMarker;
    public int EffectShader;
    public int NoVertices;
    public int Unreachable;
    public int HiddenAncestor;
    public int MarkerAncestor;
    public int MismatchedStrips;
    public int StripFieldsMissing;
    public bool HiddenIgnored;
    public readonly SortedDictionary<string, int> Unsupported = new(StringComparer.Ordinal);

    public override string ToString()
    {
        var unsupported = Unsupported.Count == 0
            ? "0"
            : $"{Unsupported.Values.Sum()} [{string.Join(", ", Unsupported.Select(kv => $"{kv.Key} x{kv.Value}"))}]";
        return $"{Shapes} shapes: counted={Counted}, hidden={Hidden}, editorMarker={EditorMarker}, effectShader={EffectShader}, "
            + $"unsupported={unsupported}, noVertices={NoVertices}, unreachable={Unreachable}, "
            + $"hiddenAncestor={HiddenAncestor}, markerAncestor={MarkerAncestor}, mismatchedStrips={MismatchedStrips}"
            + (HiddenIgnored ? ", hidden flag ignored in 2nd pass" : string.Empty);
    }
}

/// <summary>Running AABB of accepted points; the first rejected point records why the mesh is invalid.</summary>
internal sealed class BoundsAccumulator
{
    public bool Any { get; private set; }
    public Vector3 Min { get; private set; } = new(float.PositiveInfinity);
    public Vector3 Max { get; private set; } = new(float.NegativeInfinity);
    public string? Invalid { get; private set; }

    /// <summary>
    /// Largest accepted absolute mesh coordinate. Real meshes stay far below it; larger values come
    /// from broken exports and would blow up spatial grids and triangle indexes.
    /// </summary>
    public const float MaxCoordinate = 1e6f;

    public bool AddPoint(Vector3 p)
    {
        if (!Vectors.IsWithinLimit(p, MaxCoordinate))
        {
            Invalid ??= Vectors.IsFinite(p)
                ? $"vertex coordinates beyond ±{MaxCoordinate:0} units"
                : "non-finite vertex coordinates";
            return false;
        }
        Min = Vector3.Min(Min, p);
        Max = Vector3.Max(Max, p);
        Any = true;
        return true;
    }
}

/// <summary>Result of one pass over a NIF's shapes. Vertices/Indices/PartFirstTriangles are null when triangles were not requested.</summary>
internal sealed record ShapeCollection(
    BoundsAccumulator Bounds, List<Vector3>? Vertices, List<int>? Indices, List<int>? PartFirstTriangles, ShapeStats Stats);

/// <summary>
/// One pass over all shapes of a NIF: bounds (and optionally root-space triangles) of every solid
/// render shape reachable from the root through visible, non-marker nodes, with per-reason skip counts.
/// </summary>
internal sealed class NifShapeCollector
{
    private readonly RenderGeometryTypes _renderTypes;
    private readonly List<INiObject> _blocks;
    private readonly IReadOnlyDictionary<int, int> _parentOf;
    private readonly AvObjectFlags _flags;
    private readonly ShapeInclusion _inclusion;
    private readonly NodeTransformResolver _nodes;
    private readonly BoundsAccumulator _bounds = new();
    private readonly List<Vector3>? _vertices;
    private readonly List<int>? _indices;
    private readonly List<int>? _partFirstTriangles;
    private readonly ShapeStats _stats;

    private NifShapeCollector(
        RenderGeometryTypes renderTypes,
        List<INiObject> blocks,
        IReadOnlyDictionary<int, int> parentOf,
        int rootIndex,
        AvObjectFlags flags,
        ShapeInclusion inclusion,
        bool includeTriangles)
    {
        _renderTypes = renderTypes;
        _blocks = blocks;
        _parentOf = parentOf;
        _flags = flags;
        _inclusion = inclusion;
        _nodes = new NodeTransformResolver(blocks, parentOf, rootIndex, flags, inclusion);
        _vertices = includeTriangles ? [] : null;
        _indices = includeTriangles ? [] : null;
        _partFirstTriangles = includeTriangles ? [] : null;
        _stats = new ShapeStats { HiddenIgnored = inclusion.IncludeHidden };
    }

    public static ShapeCollection Collect(
        RenderGeometryTypes renderTypes,
        List<INiObject> blocks,
        IReadOnlyDictionary<int, int> parentOf,
        int rootIndex,
        AvObjectFlags flags,
        ShapeInclusion inclusion,
        bool includeTriangles) =>
        new NifShapeCollector(renderTypes, blocks, parentOf, rootIndex, flags, inclusion, includeTriangles).CollectAll();

    private ShapeCollection CollectAll()
    {
        for (var blockIndex = 0; blockIndex < _blocks.Count && _bounds.Invalid == null; blockIndex++)
        {
            if (_blocks[blockIndex] is INiShape shape) CollectShape(blockIndex, shape);
        }
        return new ShapeCollection(_bounds, _vertices, _indices, _partFirstTriangles, _stats);
    }

    private void CollectShape(int blockIndex, INiShape shape)
    {
        _stats.Shapes++;
        if (!TryGetShapeToRoot(blockIndex, shape, out var toRoot)) return;

        if (NifShapes.GetVerticesOrNull(shape) is { Count: > 0 } vertices)
        {
            AddShapeVertices(shape, vertices, toRoot);
        }
        else
        {
            AddShapeSphere(shape, toRoot);
        }
    }

    /// <summary>False (with the skip reason counted) when the shape is not counted at all.</summary>
    private bool TryGetShapeToRoot(int blockIndex, INiShape shape, out Similarity toRoot)
    {
        toRoot = Similarity.Identity;
        if (!_renderTypes.IsRenderGeometry(shape))
        {
            var name = shape.GetType().Name;
            _stats.Unsupported[name] = _stats.Unsupported.GetValueOrDefault(name) + 1;
            return false;
        }
        var kind = NifShapes.KindOf(shape, _blocks, _inclusion);
        if (!_inclusion.CountedKinds.Contains(kind))
        {
            CountSkippedKind(kind);
            return false;
        }
        if (!_inclusion.IncludeHidden && _flags.IsHiddenWithoutController(shape.Flags_ui, shape.Flags_us, shape.Controller))
        {
            _stats.Hidden++;
            return false;
        }
        if (!_parentOf.TryGetValue(blockIndex, out var parentIndex))
        {
            _stats.Unreachable++;
            return false;
        }

        var parent = _nodes.Resolve(parentIndex);
        switch (parent.Skip)
        {
            case NodeSkip.Unreachable: _stats.Unreachable++; return false;
            case NodeSkip.Hidden: _stats.HiddenAncestor++; return false;
            case NodeSkip.Marker: _stats.MarkerAncestor++; return false;
        }
        toRoot = parent.ToRoot.After(Similarity.From(shape.Translation, shape.Rotation, shape.Scale));
        return true;
    }

    private void CountSkippedKind(MeshShapeKind kind)
    {
        switch (kind)
        {
            case MeshShapeKind.EditorMarker: _stats.EditorMarker++; break;
            case MeshShapeKind.EffectShader: _stats.EffectShader++; break;
            default: throw new InvalidOperationException($"Shapes of kind {kind} cannot be left out of the geometry.");
        }
    }

    private void AddShapeVertices(INiShape shape, List<Vector3> vertices, Similarity toRoot)
    {
        var baseIndex = _vertices?.Count ?? 0;
        var added = false;
        foreach (var v in vertices)
        {
            var rootVertex = toRoot.Apply(v);
            added |= _bounds.AddPoint(rootVertex);
            _vertices?.Add(rootVertex);
        }
        if (added) _stats.Counted++;
        else _stats.NoVertices++;
        if (_indices != null && _partFirstTriangles != null) AppendPart(shape, baseIndex, vertices.Count, _indices, _partFirstTriangles);
    }

    private void AppendPart(INiShape shape, int baseIndex, int vertexCount, List<int> allIndices, List<int> partFirstTriangles)
    {
        var firstIndex = allIndices.Count;
        AppendTriangles(shape, baseIndex, vertexCount, allIndices);
        if (allIndices.Count > firstIndex) partFirstTriangles.Add(firstIndex / 3);
    }

    /// <summary>
    /// Out-of-range vertices need no filtering here: any of them already makes the whole read fail.
    /// Triangles with out-of-range indices are dropped. A shape without a triangle list contributes
    /// each vertex as a degenerate point triangle.
    /// </summary>
    private void AppendTriangles(INiShape shape, int baseIndex, int vertexCount, List<int> allIndices)
    {
        var triangles = NifShapes.GetTriangles(shape, out var stripFault);
        if (stripFault == StripFault.LengthsMismatch) _stats.MismatchedStrips++;
        if (stripFault == StripFault.FieldsMissing) _stats.StripFieldsMissing++;
        if (triangles is not { Count: > 0 })
        {
            AppendPointTriangles(baseIndex, vertexCount, allIndices);
            return;
        }

        foreach (var triangle in triangles)
        {
            int a = triangle.V1, b = triangle.V2, c = triangle.V3;
            if (a >= vertexCount || b >= vertexCount || c >= vertexCount) continue;
            allIndices.Add(baseIndex + a);
            allIndices.Add(baseIndex + b);
            allIndices.Add(baseIndex + c);
        }
    }

    private static void AppendPointTriangles(int baseIndex, int vertexCount, List<int> allIndices)
    {
        for (var i = 0; i < vertexCount; i++)
        {
            allIndices.Add(baseIndex + i);
            allIndices.Add(baseIndex + i);
            allIndices.Add(baseIndex + i);
        }
    }

    /// <summary>
    /// A sphere stays a sphere under a similarity transform, so transforming the center and scaling
    /// the radius gives an exact, rotation-invariant AABB. Legacy NiGeometry without a data block and
    /// zero/negative/non-finite radii are ignored (they would add a stray point at the shape origin).
    /// </summary>
    private void AddShapeSphere(INiShape shape, Similarity toRoot)
    {
        var sphere = shape.Bounds;
        if ((shape is not BSTriShape && shape.GeometryData == null)
            || !(sphere.Radius > 0) || !float.IsFinite(sphere.Radius))
        {
            _stats.NoVertices++;
            return;
        }
        var center = toRoot.Apply(sphere.Center);
        var extent = new Vector3(MathF.Abs(sphere.Radius * toRoot.Scale));
        _bounds.AddPoint(center - extent);
        _bounds.AddPoint(center + extent);
        _stats.Counted++;
    }
}
