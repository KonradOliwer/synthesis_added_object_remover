using NiflySharp;
using NiflySharp.Blocks;

namespace AddedObjectRemover;

/// <summary>Reads a NIF's render geometry (bounds and optionally triangles) in root-node space, and its collision layers, with NiflySharp.</summary>
internal sealed class NifGeometryReader
{
    private const string WarmUpFailedMessage =
        "NiflySharp could not load the minimal NIF it had just saved, so its block-type cache was not built and meshes cannot be read safely in parallel.";

    /// <summary>
    /// NifFile.Load lazily fills NiflySharp's own static, unlocked block-type cache, which every reader in the
    /// process shares, so this lock must be process-wide too. The cache is built under the lock, after which
    /// loads are parallel-safe.
    /// </summary>
    private static readonly object WarmUpLock = new();

    private readonly ComputedOnce<bool> _loaderWarmUp = new(PrimeBlockTypeCache);
    private readonly RenderGeometryTypes _renderTypes = new();

    /// <summary>Warms the loader up now, so a library defect ends the run before the first mesh is read.</summary>
    public void WarmUpLoader() => _ = _loaderWarmUp.Value;

    /// <summary>
    /// Loads a NIF from memory and computes the AABB, in the local space of the root node picked by
    /// <see cref="NifRootFinder"/>, of all
    /// render-geometry vertices, applying parent transforms cumulatively up to (but not including)
    /// the root node's own transform: the engine overwrites the root's local transform with the
    /// placed reference's position/rotation/scale, so it has no effect in game. Only shapes
    /// reachable from the root node are counted (orphan blocks are never rendered). Collision and
    /// particle systems are always left out of the geometry; which other shapes count is up to <paramref name="inclusion"/>,
    /// which also decides the marker nodes whose collision is left out (<see cref="NifCollisionLayers"/>).
    /// With <paramref name="includeTriangles"/>, also returns the root-space vertices and triangles
    /// of every counted shape (shapes that only have a bounding sphere add to the bounds only).
    /// A vertex that is non-finite or beyond <see cref="BoundsAccumulator.MaxCoordinate"/> makes the whole
    /// mesh <see cref="MeshReadStatus.Failed"/>. Malformed files are reported, not thrown.
    /// </summary>
    public NifReadResult ReadGeometry(byte[] data, bool includeTriangles, ShapeInclusion inclusion)
    {
        WarmUpLoader();
        try
        {
            return LoadAndReadGeometry(data, includeTriangles, inclusion);
        }
        catch (MalformedNifException ex)
        {
            return NifReadResult.Failed(MeshReadFailure.Malformed(ex.LibraryExceptionType, ex.Message));
        }
    }

    /// <summary>
    /// Builds NiflySharp's static block-type cache by saving and re-loading a minimal in-memory NIF
    /// (one root NiNode). The cache exists once that load read a block; a load that read none is a library defect.
    /// </summary>
    /// <returns>True; the value only marks the warm-up as done.</returns>
    private static bool PrimeBlockTypeCache()
    {
        lock (WarmUpLock)
        {
            var template = new NifFile(NiVersion.GetSSE(), withRootNode: true);
            using var stream = new MemoryStream();
            var saved = template.Save(stream) == 0;
            stream.Position = 0;
            var nif = new NifFile();
            return saved && nif.Load(stream) == 0 && nif.Valid && nif.Blocks.Count > 0
                ? true
                : throw new InvalidOperationException(WarmUpFailedMessage);
        }
    }

    private NifReadResult LoadAndReadGeometry(byte[] data, bool includeTriangles, ShapeInclusion inclusion)
    {
        using var stream = new MemoryStream(data, writable: false);
        var nif = new NifFile();
        var loadResult = NiflyCalls.Call(() => nif.Load(stream));
        if (loadResult != 0 || !nif.Valid)
        {
            return NifReadResult.Failed(MeshReadFailure.NotLoadable(loadResult));
        }
        if (NifRootFinder.Find(nif, data) is not { } root)
        {
            return NifReadResult.Failed(MeshReadFailure.NoRootNode());
        }

        var blocks = nif.Blocks;
        var parentOf = BuildParentMap(blocks);
        var flags = AvObjectFlags.For(nif);
        var shapes = CollectWithRetry(blocks, parentOf, flags, root.Index, includeTriangles, inclusion);
        var result = ToResult(shapes);
        var measured = !includeTriangles && result.Status == MeshReadStatus.EffectOnly
            ? MeasureEffects(shapes.Stats.ToCounts(), blocks, parentOf, flags, root.Index, inclusion)
            : result;
        return measured with
        {
            FooterRoot = root.DiffersFromLibraryRoot ? root : null,
            StripFieldsMissing = shapes.Stats.StripFieldsMissing > 0,
            CollisionLayers = NifCollisionLayers.Collect(blocks, parentOf, root.Index, flags, inclusion),
        };
    }

    private static NifReadResult ToResult(ShapeCollection shapes)
    {
        if (shapes.Bounds.Invalid is { } fault)
        {
            return NifReadResult.Failed(MeshReadFailure.InvalidCoordinates(fault));
        }
        if (!shapes.Bounds.Any && shapes.Stats.EffectShader > 0)
        {
            return NifReadResult.WithEffectsOnly(shapes.Stats.ToCounts());
        }
        if (!shapes.Bounds.Any)
        {
            return NifReadResult.WithoutRenderGeometry(shapes.Stats.ToCounts());
        }

        var geometry = new NifGeometry(
            shapes.Bounds.Min,
            shapes.Bounds.Max,
            new MeshTriangles(
                shapes.Vertices?.ToArray() ?? [],
                shapes.Indices?.ToArray() ?? [],
                shapes.PartFirstTriangles?.ToArray() ?? []));
        return NifReadResult.Succeeded(geometry, shapes.Stats.MismatchedStrips);
    }

    /// <summary>
    /// The result with the bounds of the mesh's effect shapes, collected as <paramref name="inclusion"/> says but counting only effect
    /// shapes. Effect shapes that hold no point are drawn nowhere (unreachable, hidden, or under a marker node), so the mesh has no
    /// render geometry, as a mesh of such solid shapes has none; effect shapes with invalid coordinates fail the read, as solid ones do.
    /// </summary>
    /// <param name="shapes">How the first pass, which left the effect shapes out, counted the mesh's shapes.</param>
    private NifReadResult MeasureEffects(
        ShapeCounts shapes, List<INiObject> blocks, Dictionary<int, int> parentOf, AvObjectFlags flags, int rootIndex, ShapeInclusion inclusion)
    {
        var effects = CollectWithRetry(blocks, parentOf, flags, rootIndex, includeTriangles: false, inclusion.WithOnlyEffectShapesCounted());
        if (effects.Bounds.Invalid is { } fault) return NifReadResult.Failed(MeshReadFailure.InvalidCoordinates(fault));
        if (!effects.Bounds.Any) return NifReadResult.WithoutRenderGeometry(shapes);
        return NifReadResult.WithEffectsOnly(shapes) with { EffectBox = Box.FromCorners(effects.Bounds.Min, effects.Bounds.Max) };
    }

    /// <summary>
    /// First collects as <paramref name="inclusion"/> says. If that leaves nothing but some shapes were hidden and the
    /// inclusion allows it, a second pass includes the hidden shapes: the mesh is certainly rendered somehow, and OBND would be worse.
    /// </summary>
    private ShapeCollection CollectWithRetry(
        List<INiObject> blocks, Dictionary<int, int> parentOf, AvObjectFlags flags, int rootIndex, bool includeTriangles, ShapeInclusion inclusion)
    {
        var shapes = NifShapeCollector.Collect(_renderTypes, blocks, parentOf, rootIndex, flags, inclusion, includeTriangles);
        if (inclusion.RetryIncludingHiddenWhenEmpty
            && shapes.Bounds.Invalid == null && !shapes.Bounds.Any && shapes.Stats.Hidden + shapes.Stats.HiddenAncestor > 0)
        {
            shapes = NifShapeCollector.Collect(_renderTypes, blocks, parentOf, rootIndex, flags, inclusion.WithHiddenIncluded(), includeTriangles);
        }
        return shapes;
    }

    /// <summary>
    /// Child block index -> parent NiNode block index. As in NifFile.GetParentNode, the first
    /// NiNode (in block order) listing the child wins and a node never counts as its own parent.
    /// </summary>
    private static Dictionary<int, int> BuildParentMap(List<INiObject> blocks)
    {
        var parentOf = new Dictionary<int, int>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not NiNode node || node.Children == null) continue;
            foreach (var childIndex in node.Children.Indices)
            {
                if (childIndex != i && childIndex >= 0 && childIndex < blocks.Count)
                    parentOf.TryAdd(childIndex, i);
            }
        }
        return parentOf;
    }
}
