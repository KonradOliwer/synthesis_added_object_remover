using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Structs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Computes the axis-aligned bounding box (in NIF root-node space) of a NIF's render
/// geometry using NiflySharp. Collision shapes are ignored.
/// </summary>
public static class NifBoundsReader
{
    // NifFile lazily builds a static block-type-name cache (NifFile._blockTypesByBinaryName)
    // the first time any NifFile.Load runs, with no locking around the build/read. Concurrent
    // first-time loads from multiple threads would race on that cache, so all load+process
    // work below is serialized under this lock (NifFile instances themselves are not shared,
    // only that static cache is a concern).
    private static readonly object LoadLock = new();

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
        error = null;

        try
        {
            lock (LoadLock)
            {
                // NifFile.Load has no ReadOnlySpan<byte>/byte[] overload, so copy into a stream.
                using var stream = new MemoryStream(data.ToArray(), writable: false);
                var nif = new NifFile();
                int result = nif.Load(stream); // 0 = success, >0 = error code.
                if (result != 0 || !nif.Valid)
                {
                    error = $"NifFile.Load failed with code {result}.";
                    return false;
                }

                var rootNode = nif.GetRootNode();
                if (rootNode == null || !nif.GetBlockIndex(rootNode, out int rootIndex))
                {
                    error = "NIF has no root NiNode.";
                    return false;
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

                // node block index -> cumulative node-space -> root-space transform, or null when
                // the node is unreachable from the root, hidden, an editor marker, has such an
                // ancestor, or sits in a cyclic/implausibly deep chain.
                var nodeTransforms = new Dictionary<int, Similarity?> { [rootIndex] = Similarity.Identity };

                Similarity? ResolveNode(int nodeIndex)
                {
                    var path = new List<int>();
                    Similarity? current;
                    int cursor = nodeIndex;
                    while (true)
                    {
                        if (nodeTransforms.TryGetValue(cursor, out current)) break;
                        if (path.Count >= MaxDepth || path.Contains(cursor)) { current = null; break; }
                        path.Add(cursor);
                        if (!parentOf.TryGetValue(cursor, out cursor)) { current = null; break; }
                    }

                    // Fold from the top-most unresolved ancestor down to the requested node.
                    for (int i = path.Count - 1; i >= 0; i--)
                    {
                        if (current is { } parentTransform && blocks[path[i]] is NiNode node
                            && !IsHidden(node.Flags_ui, node.Flags_us, useFlagsUi)
                            && !IsEditorMarker(node.Name?.String))
                        {
                            current = parentTransform.Then(Similarity.From(node.Translation, node.Rotation, node.Scale));
                        }
                        else
                        {
                            current = null;
                        }
                        nodeTransforms[path[i]] = current;
                    }
                    return current;
                }

                bool any = false;
                Vector3 runningMin = new(float.PositiveInfinity);
                Vector3 runningMax = new(float.NegativeInfinity);

                void AddPoint(Vector3 p)
                {
                    if (!Geometry.IsFinite(p)) return;
                    runningMin = Vector3.Min(runningMin, p);
                    runningMax = Vector3.Max(runningMax, p);
                    any = true;
                }

                for (int shapeIndex = 0; shapeIndex < blocks.Count; shapeIndex++)
                {
                    if (blocks[shapeIndex] is not INiShape shape) continue;
                    if (!IsRenderGeometry(shape)) continue;

                    // Bit 0x1 of the AV object's Flags is the standard NIF "hidden" flag.
                    if (IsHidden(shape.Flags_ui, shape.Flags_us, useFlagsUi)) continue;
                    if (IsEditorMarker(shape.Name?.String)) continue;

                    // Only shapes reachable from the root, through visible, non-marker nodes.
                    if (!parentOf.TryGetValue(shapeIndex, out int parentIndex)) continue;
                    if (ResolveNode(parentIndex) is not { } parentTransform) continue;
                    var toRoot = parentTransform.Then(Similarity.From(shape.Translation, shape.Rotation, shape.Scale));

                    if (TryGetVertices(shape) is { Count: > 0 } verts)
                    {
                        foreach (var v in verts) AddPoint(toRoot.Apply(v));
                        continue;
                    }

                    // Fallback: the shape's stored bounding sphere (local space). A sphere stays a
                    // sphere under a similarity transform, so transform the center and scale the
                    // radius: the result is an exact, rotation-invariant AABB. Legacy NiGeometry
                    // without a data block, and zero/negative/non-finite radii, are ignored (they
                    // would otherwise add a stray point at the shape origin).
                    if (shape is not BSTriShape && shape.GeometryData == null) continue;
                    BoundingSphere bounds = shape.Bounds;
                    if (!(bounds.Radius > 0) || !float.IsFinite(bounds.Radius)) continue;
                    Vector3 center = toRoot.Apply(bounds.Center);
                    Vector3 extent = new(MathF.Abs(bounds.Radius * toRoot.Scale));
                    AddPoint(center - extent);
                    AddPoint(center + extent);
                }

                if (!any)
                {
                    error = "NIF contains no visible render geometry.";
                    return false;
                }

                min = runningMin;
                max = runningMax;
                return true;
            }
        }
        catch (Exception ex)
        {
            error = $"Exception while reading NIF bounds: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Real triangle geometry only: BSTriShape and subclasses, and legacy NiTriShape/NiTriStrips
    /// (NiTriBasedGeom). The NiParticles family (NiParticleSystem, BSStripParticleSystem, ...)
    /// also derives from NiGeometry but has no usable vertices, so it is skipped. Class names are
    /// matched by string because only some NiflySharp block classes have hand-written partials.
    /// </summary>
    private static bool IsRenderGeometry(INiShape shape)
    {
        for (var type = shape.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            if (type.Name.Contains("Particle", StringComparison.Ordinal)) return false;
        }
        if (shape is BSTriShape || shape is NiTriShape) return true;
        if (shape is not NiGeometry) return true; // Other non-legacy shape families (e.g. BSGeometry).
        for (var type = shape.GetType(); type != null && type != typeof(object); type = type.BaseType)
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
    // TryReadBounds) is the standard NIF "hidden" flag.
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
