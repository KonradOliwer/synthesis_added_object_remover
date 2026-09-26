using NiflySharp;
using NiflySharp.Blocks;

namespace AddedObjectRemover;

/// <summary>Reads a NIF's render geometry (bounds and optionally triangles) in root-node space with NiflySharp.</summary>
internal static class NifGeometryReader
{
    /// <summary>
    /// NifFile.Load lazily fills a static, unlocked block-type cache; it is built once under
    /// LoadLock, after which loads are parallel-safe. If that fails, every load is serialized.
    /// </summary>
    private static readonly object LoadLock = new();
    private static readonly Lazy<bool> ParallelLoadsSafe = new(TryPrimeBlockTypeCache, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Loads a NIF from memory and computes the AABB, in the NIF root node's local space, of all
    /// render-geometry vertices, applying parent transforms cumulatively up to (but not including)
    /// the root node's own transform: the engine overwrites the root's local transform with the
    /// placed reference's position/rotation/scale, so it has no effect in game. Only shapes
    /// reachable from the root node are counted (orphan blocks are never rendered). Collision and
    /// particle systems, hidden shapes/nodes and "EditorMarker" shapes/nodes are skipped.
    /// With <paramref name="includeTriangles"/>, also returns the root-space vertices and triangles
    /// of every counted shape (shapes that only have a bounding sphere add to the bounds only).
    /// A vertex that is non-finite or beyond <see cref="Geometry.MaxCoordinate"/> makes the whole
    /// mesh <see cref="NifReadStatus.Failed"/>. Malformed files are reported, not thrown.
    /// </summary>
    public static NifReadStatus ReadGeometry(
        byte[] data,
        bool includeTriangles,
        out NifGeometry? geometry,
        out string? error)
    {
        try
        {
            if (ParallelLoadsSafe.Value)
            {
                return ReadGeometryUnlocked(data, includeTriangles, out geometry, out error);
            }
            lock (LoadLock)
            {
                return ReadGeometryUnlocked(data, includeTriangles, out geometry, out error);
            }
        }
        catch (Exception ex) when (ExpectedFailures.IsMalformedNif(ex))
        {
            geometry = null;
            error = $"{ex.GetType().Name} while reading NIF bounds: {ex.Message}";
            return NifReadStatus.Failed;
        }
    }

    /// <summary>
    /// Builds NiflySharp's static block-type cache by saving and re-loading a minimal in-memory NIF
    /// (one root NiNode). True if that load read a block, i.e. the cache now exists.
    /// </summary>
    private static bool TryPrimeBlockTypeCache()
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
            catch (Exception ex) when (ExpectedFailures.IsMalformedNif(ex))
            {
                Console.WriteLine(
                    $"Warning: NIF loader warm-up failed ({ex.GetType().Name}: {ex.Message}); meshes are parsed one at a time (slower).");
                return false;
            }
        }
    }

    private static NifReadStatus ReadGeometryUnlocked(
        byte[] data,
        bool includeTriangles,
        out NifGeometry? geometry,
        out string? error)
    {
        geometry = null;
        using var stream = new MemoryStream(data, writable: false);
        if (!TryLoad(stream, out var nif, out var rootIndex, out error)) return NifReadStatus.Failed;

        var shapes = CollectWithHiddenFallback(nif, rootIndex, includeTriangles);
        if (shapes.Bounds.Invalid != null)
        {
            error = $"NIF has {shapes.Bounds.Invalid}.";
            return NifReadStatus.Failed;
        }
        if (!shapes.Bounds.Any)
        {
            error = $"NIF contains no visible render geometry ({shapes.Stats}).";
            return NifReadStatus.NoRenderGeometry;
        }

        geometry = new NifGeometry(
            shapes.Bounds.Min,
            shapes.Bounds.Max,
            shapes.Vertices?.ToArray() ?? [],
            shapes.Indices?.ToArray() ?? []);
        return NifReadStatus.Success;
    }

    private static bool TryLoad(MemoryStream stream, out NifFile nif, out int rootIndex, out string? error)
    {
        nif = new NifFile();
        var result = nif.Load(stream); // 0 = success, >0 = error code.
        if (result != 0 || !nif.Valid)
        {
            rootIndex = -1;
            error = $"NifFile.Load failed with code {result}.";
            return false;
        }

        var rootNode = nif.GetRootNode();
        if (rootNode == null || !nif.GetBlockIndex(rootNode, out rootIndex))
        {
            rootIndex = -1;
            error = "NIF has no root NiNode.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// First honours the hidden flag. If that leaves nothing but some shapes were hidden, a second
    /// pass ignores the hidden flag: the mesh is certainly rendered somehow, and OBND would be worse.
    /// </summary>
    private static ShapeCollection CollectWithHiddenFallback(NifFile nif, int rootIndex, bool includeTriangles)
    {
        var blocks = nif.Blocks;
        var parentOf = BuildParentMap(blocks);
        var flags = AvObjectFlags.For(nif);

        var shapes = NifShapeCollector.Collect(blocks, parentOf, rootIndex, flags, includeHidden: false, includeTriangles);
        if (shapes.Bounds.Invalid == null && !shapes.Bounds.Any && shapes.Stats.Hidden + shapes.Stats.HiddenAncestor > 0)
        {
            shapes = NifShapeCollector.Collect(blocks, parentOf, rootIndex, flags, includeHidden: true, includeTriangles);
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
