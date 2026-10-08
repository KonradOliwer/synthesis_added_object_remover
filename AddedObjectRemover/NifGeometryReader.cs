using NiflySharp;
using NiflySharp.Blocks;

namespace AddedObjectRemover;

/// <summary>Reads a NIF's render geometry (bounds and optionally triangles) in root-node space with NiflySharp.</summary>
internal sealed class NifGeometryReader
{
    /// <summary>
    /// NifFile.Load lazily fills NiflySharp's own static, unlocked block-type cache, which every reader in the
    /// process shares, so this lock must be process-wide too. The cache is built once under the lock, after
    /// which loads are parallel-safe. If that fails, every load is serialized.
    /// </summary>
    private static readonly object LoadLock = new();

    private readonly ComputedOnce<WarmUp> _loaderWarmUp;
    private readonly RenderGeometryTypes _renderTypes = new();

    public NifGeometryReader()
        : this(PrimeBlockTypeCache)
    {
    }

    /// <param name="primeBlockTypeCache">Builds the loader's block-type cache; returns why it failed, or null when it worked.</param>
    internal NifGeometryReader(Func<string?> primeBlockTypeCache)
    {
        _loaderWarmUp = new ComputedOnce<WarmUp>(() => new WarmUp(primeBlockTypeCache()));
    }

    /// <summary>Warms the loader up now, so a failure is known before the first mesh is read.</summary>
    public void WarmUpLoader() => _ = _loaderWarmUp.Value;

    /// <summary>
    /// Why this reader's warm-up failed; null when it succeeded or has not run yet. Each run reports it
    /// itself, so the report does not depend on which run in the process warmed up first.
    /// </summary>
    public string? LoaderWarmUpFailure => HasWarmedUp ? _loaderWarmUp.Value.Failure : null;

    public bool HasWarmedUp => _loaderWarmUp.IsComputed;

    public int ShapeClassesDecided => _renderTypes.ShapeClassesDecided;

    private sealed record WarmUp(string? Failure)
    {
        public bool ParallelLoadsSafe => Failure == null;
    }

    /// <summary>
    /// Loads a NIF from memory and computes the AABB, in the local space of the root node picked by
    /// <see cref="NifRootFinder"/>, of all
    /// render-geometry vertices, applying parent transforms cumulatively up to (but not including)
    /// the root node's own transform: the engine overwrites the root's local transform with the
    /// placed reference's position/rotation/scale, so it has no effect in game. Only shapes
    /// reachable from the root node are counted (orphan blocks are never rendered). Collision and
    /// particle systems are always skipped; which other shapes count is up to <paramref name="inclusion"/>.
    /// With <paramref name="includeTriangles"/>, also returns the root-space vertices and triangles
    /// of every counted shape (shapes that only have a bounding sphere add to the bounds only).
    /// A vertex that is non-finite or beyond <see cref="BoundsAccumulator.MaxCoordinate"/> makes the whole
    /// mesh <see cref="MeshReadStatus.Failed"/>. Malformed files are reported, not thrown.
    /// </summary>
    public NifReadResult ReadGeometry(byte[] data, bool includeTriangles, ShapeInclusion inclusion)
    {
        try
        {
            if (_loaderWarmUp.Value.ParallelLoadsSafe)
            {
                return ReadGeometryUnlocked(data, includeTriangles, inclusion);
            }
            lock (LoadLock)
            {
                return ReadGeometryUnlocked(data, includeTriangles, inclusion);
            }
        }
        catch (MalformedNifException ex)
        {
            return NifReadResult.Failed(ex.LibraryExceptionType, $"{ex.LibraryExceptionType} while reading NIF: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds NiflySharp's static block-type cache by saving and re-loading a minimal in-memory NIF
    /// (one root NiNode). Safe if that load read a block, i.e. the cache now exists.
    /// </summary>
    private static string? PrimeBlockTypeCache()
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
                if (saved && nif.Load(stream) == 0 && nif.Valid && nif.Blocks.Count > 0) return null;
                return "the minimal NIF it saved and loaded again held no blocks";
            }
            catch (Exception ex) when (Failures.IsRecoverable(ex))
            {
                return Failures.Describe(ex);
            }
        }
    }

    private NifReadResult ReadGeometryUnlocked(byte[] data, bool includeTriangles, ShapeInclusion inclusion)
    {
        using var stream = new MemoryStream(data, writable: false);
        var nif = new NifFile();
        var loadResult = NiflyCalls.Call(() => nif.Load(stream));
        if (loadResult != 0 || !nif.Valid)
        {
            return NifReadResult.Failed(NifReadResult.LoadFailedKind, $"NifFile.Load failed with code {loadResult}.");
        }
        if (NifRootFinder.Find(nif, data) is not { } root)
        {
            return NifReadResult.Failed(NifReadResult.NoRootNodeKind, "NIF has no root NiNode.");
        }

        var shapes = CollectWithRetry(nif, root.Index, includeTriangles, inclusion);
        return ToResult(shapes) with
        {
            FooterRoot = root.DiffersFromLibraryRoot ? root : null,
            StripFieldsMissing = shapes.Stats.StripFieldsMissing > 0,
        };
    }

    private static NifReadResult ToResult(ShapeCollection shapes)
    {
        if (shapes.Bounds.Invalid != null)
        {
            return NifReadResult.Failed(NifReadResult.InvalidCoordinatesKind, $"NIF has {shapes.Bounds.Invalid}.");
        }
        if (!shapes.Bounds.Any && shapes.Stats.EffectShader > 0)
        {
            return NifReadResult.WithEffectsOnly($"NIF contains only effect-shader render geometry ({shapes.Stats}).");
        }
        if (!shapes.Bounds.Any)
        {
            return NifReadResult.WithoutRenderGeometry($"NIF contains no visible render geometry ({shapes.Stats}).");
        }

        var geometry = new NifGeometry(
            shapes.Bounds.Min,
            shapes.Bounds.Max,
            new MeshTriangles(
                shapes.Vertices?.ToArray() ?? [],
                shapes.Indices?.ToArray() ?? [],
                shapes.PartFirstTriangles?.ToArray() ?? []));
        var warning = shapes.Stats.MismatchedStrips > 0
            ? $"{shapes.Stats.MismatchedStrips} NiTriStrips shape(s) whose strip lengths do not match their points are used as points."
            : null;
        return NifReadResult.Succeeded(geometry, warning);
    }

    /// <summary>
    /// First collects as <paramref name="inclusion"/> says. If that leaves nothing but some shapes were hidden and the
    /// inclusion allows it, a second pass includes the hidden shapes: the mesh is certainly rendered somehow, and OBND would be worse.
    /// </summary>
    private ShapeCollection CollectWithRetry(NifFile nif, int rootIndex, bool includeTriangles, ShapeInclusion inclusion)
    {
        var blocks = nif.Blocks;
        var parentOf = BuildParentMap(blocks);
        var flags = AvObjectFlags.For(nif);

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
