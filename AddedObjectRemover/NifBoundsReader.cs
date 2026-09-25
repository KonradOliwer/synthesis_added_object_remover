using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Extensions;
using NiflySharp.Helpers;
using NiflySharp.Structs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Render geometry of a NIF in root-node space: its bounds plus (optionally) the triangles.
/// </summary>
public sealed class NifGeometry
{
    public NifGeometry(Vector3 min, Vector3 max, Vector3[] vertices, int[] indices)
    {
        Min = min;
        Max = max;
        Vertices = vertices;
        Indices = indices;
    }

    /// <summary>AABB of all render geometry, including bounding-sphere fallbacks of shapes without vertices.</summary>
    public Vector3 Min { get; }

    public Vector3 Max { get; }

    /// <summary>Root-space vertex positions of all counted shapes (empty when triangles were not requested).</summary>
    public Vector3[] Vertices { get; }

    /// <summary>
    /// Triangle vertex indices into <see cref="Vertices"/>, three per triangle. Shapes that have
    /// vertices but no triangle list contribute each vertex as a degenerate (point) triangle.
    /// </summary>
    public int[] Indices { get; }

    public int TriangleCount => Indices.Length / 3;
}

/// <summary>Outcome of <see cref="NifBoundsReader.ReadGeometry"/>.</summary>
public enum NifReadStatus
{
    Success,

    /// <summary>The NIF could not be parsed or holds invalid data (e.g. out-of-range coordinates).</summary>
    Failed,

    /// <summary>The NIF parsed fine but has no visible render geometry (e.g. only editor-marker shapes).</summary>
    NoRenderGeometry,
}

/// <summary>
/// Computes the axis-aligned bounding box (in NIF root-node space) of a NIF's render
/// geometry using NiflySharp, and optionally the root-space triangles. Collision shapes are ignored.
/// </summary>
public static class NifBoundsReader
{
    // NifFile lazily builds a static block-type-name cache (NifFile._blockTypesByBinaryName)
    // the first time a NifFile.Load reads a block, without locking. Verified in the NiflySharp
    // source (package "Nifly" 1.1.0): that dictionary is the only static state touched while
    // loading (everything else is per-NifFile instance; Type.GetType, Activator and the
    // [GeneratedRegex] version parser are thread-safe), and once built it is only read. So one
    // load is done under LoadLock to build the cache (see WarmUp) and later loads run in
    // parallel without any lock. If the warm-up fails for any reason, every load falls back to
    // being serialized under LoadLock.
    private static readonly object LoadLock = new();
    private static readonly Lazy<bool> ParallelLoadsSafe = new(WarmUp, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Per shape CLR type: whether it is triangle render geometry (see <see cref="IsRenderGeometryType"/>).</summary>
    private static readonly ConcurrentDictionary<Type, bool> RenderGeometryTypes = new();

    /// <summary>Cycle / malformed-graph guard for the parent walk.</summary>
    private const int MaxDepth = 256;

    /// <summary>
    /// Loads a NIF from memory and computes the AABB, in the NIF root node's local space, of
    /// all render-geometry vertices, applying parent transforms cumulatively up to (but not
    /// including) the root node's own transform. The root node's local transform is excluded
    /// because the engine overwrites it with the placed reference's own position/rotation/scale
    /// when it attaches the NIF's 3D, so a non-identity transform on the root has no effect in
    /// game. Only shapes reachable from the root node are counted (loose/orphan blocks are never
    /// rendered). Collision and particle systems are ignored. Hidden shapes/nodes (per the
    /// standard NIF "hidden" flag bit) and shapes/nodes named "EditorMarker" are skipped. Never
    /// throws; failures are reported via the return value and <paramref name="error"/>.
    /// </summary>
    public static bool TryReadBounds(
        ReadOnlySpan<byte> data,
        out Vector3 min,
        out Vector3 max,
        out string? error)
    {
        min = default;
        max = default;
        if (ReadGeometry(data.ToArray(), includeTriangles: false, out var geometry, out error) != NifReadStatus.Success) return false;
        min = geometry!.Min;
        max = geometry.Max;
        return true;
    }

    /// <summary>
    /// Same shape selection and transforms as <see cref="TryReadBounds"/>, returning the bounds
    /// and, when <paramref name="includeTriangles"/> is set, the root-space vertices and
    /// triangles of every counted shape (shapes that only have a bounding sphere add to the
    /// bounds but not to the triangles). A vertex that is non-finite or beyond
    /// <see cref="Geometry.MaxCoordinate"/> makes the whole mesh <see cref="NifReadStatus.Failed"/>.
    /// Never throws.
    /// </summary>
    public static NifReadStatus ReadGeometry(
        byte[] data,
        bool includeTriangles,
        out NifGeometry? geometry,
        out string? error)
    {
        geometry = null;
        error = null;
        try
        {
            if (ParallelLoadsSafe.Value)
            {
                return ReadGeometryCore(data, includeTriangles, out geometry, out error);
            }
            lock (LoadLock)
            {
                return ReadGeometryCore(data, includeTriangles, out geometry, out error);
            }
        }
        catch (Exception ex)
        {
            geometry = null;
            error = $"Exception while reading NIF bounds: {ex.Message}";
            return NifReadStatus.Failed;
        }
    }

    /// <summary>
    /// Builds NiflySharp's static block-type cache once, under <see cref="LoadLock"/>, by saving
    /// and re-loading a minimal in-memory NIF (one root NiNode). Returns true if that load read a
    /// block, i.e. the cache now exists and parallel loads are safe.
    /// </summary>
    private static bool WarmUp()
    {
        lock (LoadLock)
        {
            try
            {
                var template = new NifFile(NiVersion.GetSSE(), withRootNode: true);
                using var stream = new MemoryStream();
                var saved = template.Save(stream) == 0;
                stream.Position = 0;
                var nif = new NifFile();
                if (saved && nif.Load(stream) == 0 && nif.Valid && nif.Blocks.Count > 0) return true;
                Console.WriteLine("Warning: NIF loader warm-up read no blocks; meshes are parsed one at a time (slower).");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Warning: NIF loader warm-up failed ({ex.GetType().Name}: {ex.Message}); meshes are parsed one at a time (slower).");
                return false;
            }
        }
    }

    private static NifReadStatus ReadGeometryCore(
        byte[] data,
        bool includeTriangles,
        out NifGeometry? geometry,
        out string? error)
    {
        geometry = null;
        error = null;

        using var stream = new MemoryStream(data, writable: false);
        var nif = new NifFile();
        int result = nif.Load(stream); // 0 = success, >0 = error code.
        if (result != 0 || !nif.Valid)
        {
            error = $"NifFile.Load failed with code {result}.";
            return NifReadStatus.Failed;
        }

        var rootNode = nif.GetRootNode();
        if (rootNode == null || !nif.GetBlockIndex(rootNode, out int rootIndex))
        {
            error = "NIF has no root NiNode.";
            return NifReadStatus.Failed;
        }

        // NiflySharp splits NiAVObject's "Flags" field in two by Bethesda stream version
        // (nif.xml: uint when #BSVER# > 26, ushort otherwise). Both LE (83) and SSE (100)
        // are above 26 and therefore use Flags_ui; older streams use Flags_us. Only the
        // field matching the file's stream version is populated; the other stays 0.
        bool useFlagsUi = nif.Header.Version?.StreamVersion > 26;

        var blocks = nif.Blocks;

        // child block index -> parent NiNode block index, built once per NIF. As in
        // NifFile.GetParentNode, the first NiNode (in block order) listing the child wins
        // and a node never counts as its own parent.
        var parentOf = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not NiNode node || node.Children == null) continue;
            foreach (int childIndex in node.Children.Indices)
            {
                if (childIndex != i && childIndex >= 0 && childIndex < blocks.Count)
                    parentOf.TryAdd(childIndex, i);
            }
        }

        // First pass honours the hidden flag. Objects with a time controller are never treated
        // as hidden: animated NIFs (furniture, carts, doors, ...) often store parts with the
        // hidden bit set and show them through a visibility controller/animation at runtime.
        // If that still leaves nothing but some shapes were hidden, a second pass ignores the
        // hidden flag altogether (the mesh is certainly rendered somehow; OBND would be worse).
        var pass = CollectShapes(blocks, parentOf, rootIndex, useFlagsUi, includeHidden: false, includeTriangles);
        if (pass.Invalid == null && !pass.Any && pass.Stats.Hidden > 0)
        {
            pass = CollectShapes(blocks, parentOf, rootIndex, useFlagsUi, includeHidden: true, includeTriangles);
        }

        if (pass.Invalid != null)
        {
            error = $"NIF has {pass.Invalid}.";
            return NifReadStatus.Failed;
        }

        if (!pass.Any)
        {
            error = $"NIF contains no visible render geometry ({pass.Stats}).";
            return NifReadStatus.NoRenderGeometry;
        }

        var (runningMin, runningMax, allVertices, allIndices) = (pass.Min, pass.Max, pass.Vertices, pass.Indices);
        geometry = new NifGeometry(
            runningMin,
            runningMax,
            allVertices?.ToArray() ?? [],
            allIndices?.ToArray() ?? []);
        return NifReadStatus.Success;
    }

    /// <summary>Why shapes of one NIF were counted or skipped; printed when a NIF yields no geometry.</summary>
    private sealed class ShapeStats
    {
        public int Shapes;
        public int Counted;
        public int Hidden;
        public int EditorMarker;
        public int NoVertices;
        public int Unreachable;
        public int HiddenAncestor;
        public int MarkerAncestor;
        public bool HiddenIgnored;
        public readonly SortedDictionary<string, int> Unsupported = new(StringComparer.Ordinal);

        public override string ToString()
        {
            var unsupported = Unsupported.Count == 0
                ? "0"
                : $"{Unsupported.Values.Sum()} [{string.Join(", ", Unsupported.Select(kv => $"{kv.Key} x{kv.Value}"))}]";
            return $"{Shapes} shapes: counted={Counted}, hidden={Hidden}, editorMarker={EditorMarker}, "
                + $"unsupported={unsupported}, noVertices={NoVertices}, unreachable={Unreachable}, "
                + $"hiddenAncestor={HiddenAncestor}, markerAncestor={MarkerAncestor}"
                + (HiddenIgnored ? ", hidden flag ignored in 2nd pass" : string.Empty);
        }
    }

    private sealed record CollectResult(
        bool Any,
        Vector3 Min,
        Vector3 Max,
        List<Vector3>? Vertices,
        List<int>? Indices,
        ShapeStats Stats,
        string? Invalid);

    private enum NodeSkip { None, Hidden, Marker, Unreachable }

    /// <summary>
    /// One pass over all shapes: bounds (and optionally triangles) of every render shape reachable
    /// from the root through visible, non-marker nodes, with per-reason skip counts.
    /// </summary>
    private static CollectResult CollectShapes(
        List<INiObject> blocks,
        Dictionary<int, int> parentOf,
        int rootIndex,
        bool useFlagsUi,
        bool includeHidden,
        bool includeTriangles)
    {
        var stats = new ShapeStats { HiddenIgnored = includeHidden };

        // node block index -> cumulative node-space -> root-space transform, or the reason the
        // node is skipped: unreachable from the root (or in a cyclic/implausibly deep chain),
        // hidden, an editor marker, or has such an ancestor.
        var nodeStates = new Dictionary<int, (Similarity Transform, NodeSkip Skip)>
        {
            [rootIndex] = (Similarity.Identity, NodeSkip.None),
        };

        (Similarity Transform, NodeSkip Skip) ResolveNode(int nodeIndex)
        {
            var path = new List<int>();
            (Similarity Transform, NodeSkip Skip) current;
            int cursor = nodeIndex;
            while (true)
            {
                if (nodeStates.TryGetValue(cursor, out current)) break;
                if (path.Count >= MaxDepth || path.Contains(cursor)) { current = (Similarity.Identity, NodeSkip.Unreachable); break; }
                path.Add(cursor);
                if (!parentOf.TryGetValue(cursor, out cursor)) { current = (Similarity.Identity, NodeSkip.Unreachable); break; }
            }

            // Fold from the top-most unresolved ancestor down to the requested node.
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (current.Skip == NodeSkip.None)
                {
                    if (blocks[path[i]] is not NiNode node)
                    {
                        current = (Similarity.Identity, NodeSkip.Unreachable);
                    }
                    else if (IsEditorMarker(node.Name?.String))
                    {
                        current = (Similarity.Identity, NodeSkip.Marker);
                    }
                    else if (!includeHidden && IsHidden(node.Flags_ui, node.Flags_us, useFlagsUi) && !HasController(node.Controller))
                    {
                        current = (Similarity.Identity, NodeSkip.Hidden);
                    }
                    else
                    {
                        current = (current.Transform.Then(Similarity.From(node.Translation, node.Rotation, node.Scale)), NodeSkip.None);
                    }
                }
                nodeStates[path[i]] = current;
            }
            return current;
        }

        bool any = false;
        Vector3 runningMin = new(float.PositiveInfinity);
        Vector3 runningMax = new(float.NegativeInfinity);
        var allVertices = includeTriangles ? new List<Vector3>() : null;
        var allIndices = includeTriangles ? new List<int>() : null;
        string? invalid = null;

        bool AddPoint(Vector3 p)
        {
            if (!Geometry.IsWithinLimits(p))
            {
                invalid ??= Geometry.IsFinite(p)
                    ? $"vertex coordinates beyond ±{Geometry.MaxCoordinate:0} units"
                    : "non-finite vertex coordinates";
                return false;
            }
            runningMin = Vector3.Min(runningMin, p);
            runningMax = Vector3.Max(runningMax, p);
            any = true;
            return true;
        }

        for (int shapeIndex = 0; shapeIndex < blocks.Count && invalid == null; shapeIndex++)
        {
            if (blocks[shapeIndex] is not INiShape shape) continue;
            stats.Shapes++;
            if (!IsRenderGeometry(shape))
            {
                var name = shape.GetType().Name;
                stats.Unsupported[name] = stats.Unsupported.GetValueOrDefault(name) + 1;
                continue;
            }

            if (IsEditorMarker(shape.Name?.String)) { stats.EditorMarker++; continue; }
            // Bit 0x1 of the AV object's Flags is the standard NIF "hidden" flag.
            if (!includeHidden && IsHidden(shape.Flags_ui, shape.Flags_us, useFlagsUi) && !HasController(shape.Controller))
            {
                stats.Hidden++;
                continue;
            }

            // Only shapes reachable from the root, through visible, non-marker nodes.
            if (!parentOf.TryGetValue(shapeIndex, out int parentIndex)) { stats.Unreachable++; continue; }
            var (parentTransform, skip) = ResolveNode(parentIndex);
            switch (skip)
            {
                case NodeSkip.Unreachable: stats.Unreachable++; continue;
                case NodeSkip.Hidden: stats.HiddenAncestor++; stats.Hidden++; continue;
                case NodeSkip.Marker: stats.MarkerAncestor++; continue;
            }
            var toRoot = parentTransform.Then(Similarity.From(shape.Translation, shape.Rotation, shape.Scale));

            if (TryGetVertices(shape) is { Count: > 0 } verts)
            {
                var added = false;
                foreach (var v in verts) added |= AddPoint(toRoot.Apply(v));
                if (added) stats.Counted++;
                else stats.NoVertices++;
                if (allVertices != null && allIndices != null)
                {
                    AddTriangles(shape, verts, toRoot, allVertices, allIndices);
                }
                continue;
            }

            // Fallback: the shape's stored bounding sphere (local space). A sphere stays a
            // sphere under a similarity transform, so transform the center and scale the
            // radius: the result is an exact, rotation-invariant AABB. Legacy NiGeometry
            // without a data block, and zero/negative/non-finite radii, are ignored (they
            // would otherwise add a stray point at the shape origin).
            BoundingSphere bounds = shape.Bounds;
            if ((shape is not BSTriShape && shape.GeometryData == null)
                || !(bounds.Radius > 0) || !float.IsFinite(bounds.Radius))
            {
                stats.NoVertices++;
                continue;
            }
            Vector3 center = toRoot.Apply(bounds.Center);
            Vector3 extent = new(MathF.Abs(bounds.Radius * toRoot.Scale));
            AddPoint(center - extent);
            AddPoint(center + extent);
            stats.Counted++;
        }

        return new CollectResult(any, runningMin, runningMax, allVertices, allIndices, stats, invalid);
    }

    private static bool HasController(NiBlockRef<NiTimeController>? controller) =>
        controller != null && !controller.IsEmpty();

    /// <summary>
    /// Triangles of a shape: the shape's own list (BSTriShape family, NiTriShape), or for legacy
    /// NiTriStrips the strips of its NiTriStripsData converted to triangles.
    /// <para>
    /// NiflySharp's source generator (NiflySharp.Generator/SourceGenUtil.cs,
    /// <c>ClassesWithoutProperties</c>) explicitly lists NiTriStripsData among the classes it does
    /// NOT generate public properties for, so there is no public API for the strip data (confirmed
    /// by a failed build: CS1061 on both <c>Points</c> and <c>StripLengths</c>). The data only
    /// exists as the protected fields <c>_points</c> (flat point/index list; its size is the sum of
    /// all entries in <c>_stripLengths</c>, per NiflySharp's own XML doc comments) and
    /// <c>_stripLengths</c>, which are read here by reflection. <see cref="ListExtensions.SplitByFlexSize"/>
    /// (used the same way by NiflySharp's own <c>SkinPartition.GetStripsLists</c>) splits the flat
    /// point list back into individual strips, and <see cref="IndicesHelper.GenerateTrianglesFromStrips"/>
    /// converts those to triangles with alternating winding, skipping degenerate (repeated-index)
    /// triangles and strips shorter than 3 points.
    /// </para>
    /// </summary>
    private static List<Triangle>? GetTriangles(INiShape shape)
    {
        try
        {
            if (shape.Triangles is { Count: > 0 } triangles) return triangles;
        }
        catch (NotImplementedException)
        {
            return null; // BSGeometry (Starfield) does not implement Triangles.
        }

        if (shape.GeometryData is not NiTriStripsData data) return null;
        if (StripPointsField == null || StripLengthsField == null)
        {
            if (Interlocked.Exchange(ref _stripFieldsMissingReported, 1) == 0)
            {
                Console.WriteLine(
                    "Warning: NiTriStripsData._points/_stripLengths not found in this NiflySharp version; "
                    + "NiTriStrips shapes are used as points in the touch test.");
            }
            return null;
        }
        try
        {
            if (StripPointsField.GetValue(data) is not List<ushort> { Count: > 0 } points
                || StripLengthsField.GetValue(data) is not List<ushort> { Count: > 0 } stripLengths)
                return null;

            var strips = points.SplitByFlexSize(stripLengths).ToList();
            return strips.Count > 0 ? IndicesHelper.GenerateTrianglesFromStrips(strips) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            return null; // Strip lengths that do not match the point list.
        }
    }

    private const System.Reflection.BindingFlags StripFieldFlags =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    private static readonly System.Reflection.FieldInfo? StripPointsField =
        typeof(NiTriStripsData).GetField("_points", StripFieldFlags);

    private static readonly System.Reflection.FieldInfo? StripLengthsField =
        typeof(NiTriStripsData).GetField("_stripLengths", StripFieldFlags);

    private static int _stripFieldsMissingReported;

    /// <summary>
    /// Appends a shape's vertices (transformed to root space) and triangles. Out-of-range vertices
    /// and triangles referencing them or out-of-range indices are dropped. A shape without a
    /// triangle list contributes each vertex as a degenerate point triangle.
    /// </summary>
    private static void AddTriangles(
        INiShape shape,
        List<Vector3> shapeVertices,
        Similarity toRoot,
        List<Vector3> allVertices,
        List<int> allIndices)
    {
        var baseIndex = allVertices.Count;
        var finite = new bool[shapeVertices.Count];
        for (int i = 0; i < shapeVertices.Count; i++)
        {
            var p = toRoot.Apply(shapeVertices[i]);
            finite[i] = Geometry.IsWithinLimits(p);
            allVertices.Add(finite[i] ? p : Vector3.Zero);
        }

        var triangles = GetTriangles(shape);
        if (triangles is not { Count: > 0 })
        {
            for (int i = 0; i < shapeVertices.Count; i++)
            {
                if (!finite[i]) continue;
                allIndices.Add(baseIndex + i);
                allIndices.Add(baseIndex + i);
                allIndices.Add(baseIndex + i);
            }
            return;
        }

        foreach (var triangle in triangles)
        {
            int a = triangle.V1, b = triangle.V2, c = triangle.V3;
            if (a >= finite.Length || b >= finite.Length || c >= finite.Length) continue;
            if (!finite[a] || !finite[b] || !finite[c]) continue;
            allIndices.Add(baseIndex + a);
            allIndices.Add(baseIndex + b);
            allIndices.Add(baseIndex + c);
        }
    }

    private static bool IsRenderGeometry(INiShape shape) =>
        RenderGeometryTypes.GetOrAdd(shape.GetType(), static type => IsRenderGeometryType(type));

    /// <summary>
    /// Real triangle geometry only: BSTriShape and subclasses, and legacy NiTriShape/NiTriStrips
    /// (NiTriBasedGeom). The NiParticles family (NiParticleSystem, BSStripParticleSystem, ...)
    /// also derives from NiGeometry but has no usable vertices, so it is skipped. Class names are
    /// matched by string because only some NiflySharp block classes have hand-written partials.
    /// The result only depends on the CLR type, so it is cached per type.
    /// </summary>
    private static bool IsRenderGeometryType(Type shapeType)
    {
        for (var type = shapeType; type != null && type != typeof(object); type = type.BaseType)
        {
            if (type.Name.Contains("Particle", StringComparison.Ordinal)) return false;
        }
        if (typeof(BSTriShape).IsAssignableFrom(shapeType) || typeof(NiTriShape).IsAssignableFrom(shapeType)) return true;
        if (!typeof(NiGeometry).IsAssignableFrom(shapeType)) return true; // Other non-legacy shape families (e.g. BSGeometry).
        for (var type = shapeType; type != null && type != typeof(object); type = type.BaseType)
        {
            if (type.Name is "NiTriBasedGeom" or "NiTriStrips") return true;
        }
        return false;
    }

    /// <summary>
    /// Vertex positions in shape space, or null when the shape carries none (caller then uses
    /// the bounding sphere).
    /// <list type="bullet">
    /// <item>BSDynamicTriShape: positions live in its dynamic vertex list (<c>Vertices</c>,
    /// Vector4 xyz). Its vertex descriptor never has the Vertex flag, and NifFile.PrepareData only
    /// copies the dynamic data into the regular vertex data for skinned shapes, so the regular
    /// vertex data of a non-skinned dynamic shape is all zeros.</item>
    /// <item>BSTriShape: regular vertex data, populated from NiSkinPartition for skinned SSE
    /// shapes by NifFile.PrepareData.</item>
    /// <item>Legacy NiTriShape/NiTriStrips: the NiGeometryData block.</item>
    /// </list>
    /// </summary>
    private static List<Vector3>? TryGetVertices(INiShape shape)
    {
        switch (shape)
        {
            case BSDynamicTriShape dynShape:
                if (dynShape.Vertices is not { Count: > 0 } dynamicVertices) return null;
                var positions = new List<Vector3>(dynamicVertices.Count);
                foreach (var v in dynamicVertices) positions.Add(new Vector3(v.X, v.Y, v.Z));
                return positions;
            case BSTriShape triShape:
                return triShape.VertexCount > 0 && (triShape.HasVertices || triShape.IsSkinned)
                    ? triShape.VertexPositions
                    : null;
            default:
                return shape.GeometryData?.Vertices;
        }
    }

    // Bit 0x1 of the AV object's Flags (Flags_ui for stream > 26, Flags_us otherwise - see
    // TryReadGeometryCore) is the standard NIF "hidden" flag.
    private static bool IsHidden(uint flagsUi, ushort flagsUs, bool useFlagsUi) =>
        ((useFlagsUi ? flagsUi : flagsUs) & 0x1) != 0;

    // Editor markers have no dedicated API; skip by the Creation Kit naming convention.
    private static bool IsEditorMarker(string? name) =>
        name != null && name.Contains("EditorMarker", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Similarity transform p' = T + S * (R * p), matching nifly's documented MatTransform
    /// semantics. R rows are taken from Matrix33 M11..M33 exactly as NiflySharp's own
    /// NiNode.TransformToParent builds them.
    /// </summary>
    private readonly record struct Similarity(Mat3 Rotation, float Scale, Vector3 Translation)
    {
        public static Similarity Identity => new(new Mat3(1, 0, 0, 0, 1, 0, 0, 0, 1), 1f, Vector3.Zero);

        public static Similarity From(Vector3 translation, Matrix33 rotation, float scale) => new(
            new Mat3(
                rotation.M11, rotation.M12, rotation.M13,
                rotation.M21, rotation.M22, rotation.M23,
                rotation.M31, rotation.M32, rotation.M33),
            scale,
            translation);

        public Vector3 Apply(Vector3 v) => Translation + Rotation.Transform(v * Scale);

        /// <summary>
        /// Composition "this after child": this(child(v)) =
        /// T + S*R*childT + (S*childS) * (R*childR) * v.
        /// </summary>
        public Similarity Then(Similarity child) => new(
            Rotation * child.Rotation,
            Scale * child.Scale,
            Translation + Rotation.Transform(child.Translation * Scale));
    }
}
