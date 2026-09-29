using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

internal readonly record struct BoundsStats(
    int BasesFromNif,
    int BasesNifFallbackToObnd,
    int BasesFromObnd,
    int BasesWithoutBounds,
    int BasesUnresolved,
    int ModelsRead,
    int ModelsFailed,
    int ModelsEffectOnly,
    int ModelsFromLooseFiles,
    int ModelsFromArchives,
    int ModelsWithFooterRoot,
    int ArchivesIndexed,
    IReadOnlyList<KeyValuePair<string, int>> ModelFailuresByKind);

/// <summary>
/// Shape facts about base objects: the local-space bounding box (measured from the NIF mesh when
/// possible, else the base record's Object Bounds), whether the base is invisible, and on demand
/// (not cached) the mesh's root-space triangles. Bounds and visibility are cached per base FormKey
/// and per normalized model path, failures included.
///
/// Thread-safe: each base is measured and each mesh read exactly once even under concurrent
/// requests.
/// </summary>
internal sealed class ShapeCatalog
{
    private readonly IBaseFacts _bases;
    private readonly MeshFileSource _meshFiles;
    private readonly AssetProblemLog _problems;
    private readonly LazyCache<FormKey, BaseShape> _byBase = new();
    private readonly LazyCache<string, MeshBounds> _byMesh = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReasonCounter _modelFailures = new();

    // Per unique base object
    private int _basesFromNif;
    private int _basesNifFallbackToObnd;
    private int _basesFromObnd;
    private int _basesWithoutBounds;
    private int _basesUnresolved;

    // Per unique mesh path
    private int _modelsRead;
    private int _modelsFailed;
    private int _modelsEffectOnly;
    private int _modelsFromLooseFiles;
    private int _modelsFromArchives;
    private int _modelsWithFooterRoot;

    public ShapeCatalog(IBaseFacts bases, MeshFileSource meshFiles, AssetProblemLog problems)
    {
        _bases = bases;
        _meshFiles = meshFiles;
        _problems = problems;
    }

    /// <param name="MeshPath">Normalized path of the mesh the bounds came from; null for OBND or none.</param>
    /// <param name="InvisibleKind">What kind of invisible object the base makes; null when it may be visible.</param>
    /// <param name="Resolved">False when the base is not in the load order.</param>
    /// <param name="EffectOnlyMesh">The base's mesh holds only effect-shader shapes, nothing solid.</param>
    /// <param name="InvisibleForLackOfGeometry">
    /// <paramref name="InvisibleKind"/> is only a marker kind guessed from the base having no geometry,
    /// not from its record type or marker flag; a primitive reference of such a base is a trigger volume.
    /// </param>
    internal sealed record BaseShape(
        Box Box,
        string? MeshPath,
        InvisibleObjectKind? InvisibleKind,
        bool Resolved = true,
        bool EffectOnlyMesh = false,
        bool InvisibleForLackOfGeometry = false);

    /// <param name="Box">Null when the mesh is unreadable or has no render geometry.</param>
    private sealed record MeshBounds(Box? Box, NifReadStatus Status);

    public BoundsStats GetStats() => new(
        Volatile.Read(ref _basesFromNif),
        Volatile.Read(ref _basesNifFallbackToObnd),
        Volatile.Read(ref _basesFromObnd),
        Volatile.Read(ref _basesWithoutBounds),
        Volatile.Read(ref _basesUnresolved),
        Volatile.Read(ref _modelsRead),
        Volatile.Read(ref _modelsFailed),
        Volatile.Read(ref _modelsEffectOnly),
        Volatile.Read(ref _modelsFromLooseFiles),
        Volatile.Read(ref _modelsFromArchives),
        Volatile.Read(ref _modelsWithFooterRoot),
        _meshFiles.ArchivesIndexed,
        _modelFailures.Snapshot());

    public void BuildArchiveIndexNow() => _meshFiles.BuildArchiveIndexNow();

    /// <summary>Unknown or missing bases yield a zero-size box at the origin.</summary>
    public Box GetLocalBox(BaseRef? baseRef) =>
        baseRef is { } reference ? GetBaseShape(reference).Box : Box.Zero;

    public void MeasureBases(IReadOnlyList<BaseRef> bases, ParallelOptions options)
    {
        Parallel.ForEach(bases, options, reference => GetBaseShape(reference));
    }

    /// <summary>Null when the bounds did not come from a readable mesh (OBND fallback, NPCs, no model).</summary>
    public string? GetMeshPath(BaseRef? baseRef) =>
        baseRef is { } reference ? GetBaseShape(reference).MeshPath : null;

    /// <summary>
    /// Whether a placed object with this base can be seen in game. A primitive box reference
    /// (trigger/activator volume) only counts as visible when its base has a visible mesh; otherwise
    /// it is a trigger box, unless the base's record type or marker flag names another invisible kind.
    /// </summary>
    public ObjectVisibility GetVisibility(BaseRef? baseRef, bool isPrimitive, bool hasMapMarker)
    {
        if (hasMapMarker) return ObjectVisibility.Invisible(InvisibleObjectKind.MapMarkers);
        if (baseRef is not { } reference) return ObjectVisibility.MissingBase;
        var shape = GetBaseShape(reference);
        if (!shape.Resolved) return ObjectVisibility.MissingBase;
        if (shape.InvisibleKind is { } kind && !(isPrimitive && shape.InvisibleForLackOfGeometry)) return ObjectVisibility.Invisible(kind);
        if (shape.EffectOnlyMesh) return ObjectVisibility.EffectOnly;
        return isPrimitive && shape.MeshPath == null
            ? ObjectVisibility.Invisible(InvisibleObjectKind.TriggerBoxes)
            : ObjectVisibility.Visible;
    }

    /// <summary>Mesh-local bounds of a normalized mesh path; null when it is unreadable or has no render geometry. Cached, failures included.</summary>
    public Box? GetMeshBox(string meshPath) => GetMeshBounds(meshPath).Box;

    /// <summary>Null when the mesh cannot be read or has no triangles.</summary>
    public NifGeometry? ReadGeometry(string meshPath)
    {
        var (result, _) = LoadAndParse(meshPath, includeTriangles: true);
        if (result.Status != NifReadStatus.Success)
        {
            _problems.Add(new AssetProblem(
                meshPath, AssetProblemKind.TrianglesUnreadable, $"  [mesh] triangles unreadable: {meshPath} ({result.Error})"));
            return null;
        }
        return result.Geometry is { TriangleCount: > 0 } geometry ? geometry : null;
    }

    private BaseShape GetBaseShape(BaseRef reference) =>
        _byBase.GetOrCreate(reference.FormKey, () => MeasureBase(reference));

    /// <summary>NIF bounds when readable, else OBND, else none.</summary>
    private BaseShape MeasureBase(BaseRef baseRef)
    {
        var facts = _bases.Of(baseRef);
        if (!facts.Resolved)
        {
            Interlocked.Increment(ref _basesUnresolved);
            return new BaseShape(Box.Zero, null, null, Resolved: false);
        }

        var hasModel = facts.ModelPath != null;

        if (GetMarkerFlagKind(facts) is { } markerKind)
        {
            return new BaseShape(Box.Zero, null, markerKind);
        }

        var meshWithoutGeometry = false;
        if (facts.ModelPath != null)
        {
            var meshPath = MeshFileSource.NormalizeMeshPath(facts.ModelPath);
            var mesh = GetMeshBounds(meshPath);
            if (mesh.Box is { } nifBox)
            {
                Interlocked.Increment(ref _basesFromNif);
                return ClassifyShape(facts, nifBox, meshPath, hasModel, meshWithoutGeometry: false);
            }
            Interlocked.Increment(ref _basesNifFallbackToObnd);
            if (mesh.Status == NifReadStatus.EffectOnly) return MeasureEffectOnlyBase(facts);
            meshWithoutGeometry = mesh.Status == NifReadStatus.NoRenderGeometry;
        }

        if (facts.ObjectBounds is { } bounds)
        {
            Interlocked.Increment(ref _basesFromObnd);
            return ClassifyShape(facts, bounds, meshPath: null, hasModel, meshWithoutGeometry);
        }

        Interlocked.Increment(ref _basesWithoutBounds);
        return ClassifyShape(facts, Box.Zero, meshPath: null, hasModel, meshWithoutGeometry);
    }

    /// <summary>OBND box when present; a light whose model is only an effect (glow, light rays) stays a light.</summary>
    private static BaseShape MeasureEffectOnlyBase(BaseFacts facts)
    {
        var kind = facts.Kind == BaseRecordKind.Light ? InvisibleObjectKind.Lights : (InvisibleObjectKind?)null;
        return new BaseShape(facts.ObjectBounds ?? Box.Zero, null, kind, EffectOnlyMesh: true);
    }

    /// <summary>
    /// Invisible are record types that never render, and bases with a mesh that parsed but has no
    /// visible render geometry (marker meshes) or with no mesh at all and zero-size bounds. NPCs
    /// always count as visible.
    /// </summary>
    internal static BaseShape ClassifyShape(BaseFacts facts, Box box, string? meshPath, bool hasModel, bool meshWithoutGeometry)
    {
        if (facts.Kind == BaseRecordKind.Npc) return new BaseShape(box, meshPath, InvisibleKind: null);
        if (GetRecordTypeInvisibleKind(facts.Kind, hasModel, meshWithoutGeometry) is { } kind) return new BaseShape(box, meshPath, kind);
        var hasNoGeometry = meshWithoutGeometry || (!hasModel && box.Size == Vector3.Zero);
        if (!hasNoGeometry) return new BaseShape(box, meshPath, InvisibleKind: null);
        var markerKind = ClassifyMarker(facts);
        return new BaseShape(box, meshPath, markerKind, InvisibleForLackOfGeometry: markerKind == InvisibleObjectKind.OtherMarkers);
    }

    private static InvisibleObjectKind? GetRecordTypeInvisibleKind(BaseRecordKind recordKind, bool hasModel, bool meshWithoutGeometry) => recordKind switch
    {
        BaseRecordKind.Light when !hasModel || meshWithoutGeometry => InvisibleObjectKind.Lights,
        BaseRecordKind.SoundMarker => InvisibleObjectKind.SoundMarkers,
        BaseRecordKind.AcousticSpace => InvisibleObjectKind.AcousticSpaces,
        BaseRecordKind.TextureSet => InvisibleObjectKind.Decals,
        BaseRecordKind.IdleMarker => InvisibleObjectKind.IdleMarkers,
        _ => null,
    };

    /// <summary>Checked before any mesh read.</summary>
    private static InvisibleObjectKind? GetMarkerFlagKind(BaseFacts facts) => facts.MarkerFlag switch
    {
        MarkerFlagKind.XMarker => InvisibleObjectKind.XMarkers,
        MarkerFlagKind.FurnitureMarker => InvisibleObjectKind.FurnitureMarkers,
        MarkerFlagKind.DoorMarker => InvisibleObjectKind.DoorMarkers,
        MarkerFlagKind.OtherMarker => ClassifyMarker(facts),
        _ => null,
    };

    private static InvisibleObjectKind ClassifyMarker(BaseFacts facts) =>
        facts.HasCritterSpawnScript ? InvisibleObjectKind.CritterSpawners : InvisibleObjectKind.OtherMarkers;

    private MeshBounds GetMeshBounds(string meshPath) =>
        _byMesh.GetOrCreate(meshPath, () => ReadMeshBounds(meshPath));

    private MeshBounds ReadMeshBounds(string meshPath)
    {
        var (result, source) = LoadAndParse(meshPath, includeTriangles: false);
        CountMeshSource(source);
        CountFooterRoot(meshPath, result.FooterRoot);

        if (result is { Status: NifReadStatus.Success, Geometry: { } geometry })
        {
            Interlocked.Increment(ref _modelsRead);
            return new MeshBounds(Box.FromCorners(geometry.Min, geometry.Max), result.Status);
        }
        if (result.Status == NifReadStatus.EffectOnly)
        {
            Interlocked.Increment(ref _modelsEffectOnly);
            return new MeshBounds(null, result.Status);
        }

        Interlocked.Increment(ref _modelsFailed);
        _modelFailures.Add(result.ErrorKind ?? result.Status.ToString());
        _problems.Add(result.ErrorKind == NifReadResult.NotFoundKind
            ? new AssetProblem(meshPath, AssetProblemKind.NotFound, $"  [mesh] not found: {meshPath}")
            : new AssetProblem(meshPath, AssetProblemKind.Unreadable, $"  [mesh] unreadable: {meshPath} ({result.Error})"));
        return new MeshBounds(null, result.Status);
    }

    private (NifReadResult Result, MeshSource Source) LoadAndParse(string meshPath, bool includeTriangles)
    {
        var (bytes, source) = _meshFiles.Load(meshPath);
        var result = bytes == null
            ? NifReadResult.Failed(NifReadResult.NotFoundKind, "mesh file not found")
            : NifGeometryReader.ReadGeometry(bytes, includeTriangles);
        if (result.Warning != null)
        {
            _problems.Add(new AssetProblem(meshPath, AssetProblemKind.ReadWarning, $"  [mesh] {meshPath}: {result.Warning}"));
        }
        return (result, source);
    }

    private void CountMeshSource(MeshSource source)
    {
        switch (source)
        {
            case MeshSource.LooseFile: Interlocked.Increment(ref _modelsFromLooseFiles); break;
            case MeshSource.Archive: Interlocked.Increment(ref _modelsFromArchives); break;
        }
    }

    private void CountFooterRoot(string meshPath, NifRoot? footerRoot)
    {
        if (footerRoot is not { } root) return;
        Interlocked.Increment(ref _modelsWithFooterRoot);
        _problems.Add(new AssetProblem(
            meshPath,
            AssetProblemKind.FooterRoot,
            $"  [mesh] {meshPath}: footer root block {root.Index} used instead of first-node root block {root.LibraryRootIndex}"));
    }
}
