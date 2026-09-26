using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

internal readonly record struct BoundsStats(
    int BasesFromNif,
    int BasesNifFallbackToObnd,
    int BasesFromObnd,
    int BasesWithoutBounds,
    int BasesUnresolved,
    int ModelsRead,
    int ModelsFailed,
    int ModelsFromLooseFiles,
    int ModelsFromArchives,
    int ArchivesIndexed);

/// <summary>
/// Shape facts about base objects: the local-space bounding box (measured from the NIF mesh when
/// possible, else the base record's Object Bounds), whether the base is invisible, and on demand
/// (not cached) the mesh's root-space triangles. Bounds and visibility are cached per base FormKey
/// and per normalized model path, failures included.
///
/// Thread-safe: each base is resolved and each mesh read exactly once even under concurrent
/// requests, and Mutagen's load-order link cache is documented as multithread safe.
/// </summary>
internal sealed class BaseObjectShapeProvider
{
    private readonly ILinkCache _linkCache;
    private readonly MeshFileSource _meshFiles;
    private readonly MeshMessageLog _messages;
    private readonly bool _useNif;
    private readonly LazyCache<FormKey, BaseShape> _byBase = new();
    private readonly LazyCache<string, MeshBounds> _byMesh = new(StringComparer.OrdinalIgnoreCase);

    // Per unique base object
    private int _basesFromNif;
    private int _basesNifFallbackToObnd;
    private int _basesFromObnd;
    private int _basesWithoutBounds;
    private int _basesUnresolved;

    // Per unique mesh path
    private int _modelsRead;
    private int _modelsFailed;
    private int _modelsFromLooseFiles;
    private int _modelsFromArchives;

    public BaseObjectShapeProvider(ILinkCache linkCache, MeshFileSource meshFiles, MeshMessageLog messages, bool useNif)
    {
        _linkCache = linkCache;
        _meshFiles = meshFiles;
        _messages = messages;
        _useNif = useNif;
    }

    /// <param name="MeshPath">Normalized path of the mesh the bounds came from; null for OBND or none.</param>
    /// <param name="InvisibleReason">Why the base is never visible in game; null when it may be visible.</param>
    private sealed record BaseShape(Box Box, string? MeshPath, string? InvisibleReason);

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
        Volatile.Read(ref _modelsFromLooseFiles),
        Volatile.Read(ref _modelsFromArchives),
        _meshFiles.ArchivesIndexed);

    public void BuildArchiveIndexNow()
    {
        if (_useNif) _meshFiles.BuildArchiveIndexNow();
    }

    /// <summary>Unknown or missing bases yield a zero-size box at the origin.</summary>
    public Box GetLocalBox(BaseRef? baseRef) =>
        baseRef is { } reference ? GetBaseShape(reference).Box : Box.Zero;

    public void ResolveBounds(IReadOnlyList<BaseRef> bases, ParallelOptions options)
    {
        Parallel.ForEach(bases, options, reference => GetBaseShape(reference));
    }

    /// <summary>Null when the bounds did not come from a readable mesh (OBND fallback, NPCs, no model).</summary>
    public string? GetMeshPath(BaseRef? baseRef) =>
        baseRef is { } reference ? GetBaseShape(reference).MeshPath : null;

    /// <summary>
    /// Why an other-mod object with this base can never be seen or collided with (a light or sound
    /// marker, a decal, a mesh with only marker geometry, ...), or null when it may be visible.
    /// A primitive box reference (trigger/activator volume) only counts when its base has a visible mesh.
    /// </summary>
    public string? GetInvisibleReason(BaseRef? baseRef, bool isPrimitive)
    {
        if (baseRef is not { } reference) return null;
        var shape = GetBaseShape(reference);
        if (shape.InvisibleReason != null) return shape.InvisibleReason;
        return isPrimitive && shape.MeshPath == null ? "trigger/activator box without visible mesh" : null;
    }

    /// <summary>Null when the mesh cannot be read or has no triangles.</summary>
    public NifGeometry? ReadGeometry(string meshPath)
    {
        var (bytes, _) = _meshFiles.Load(meshPath);
        return bytes != null
               && NifGeometryReader.ReadGeometry(bytes, includeTriangles: true, out var geometry, out _) == NifReadStatus.Success
               && geometry is { TriangleCount: > 0 }
            ? geometry
            : null;
    }

    /// <summary>
    /// Resolves by the base link's own type; <see cref="IMajorRecordGetter"/> would make the link
    /// cache enumerate every record of every mod.
    /// </summary>
    public IMajorRecordGetter? ResolveBaseOrNull(BaseRef baseRef) =>
        _linkCache.TryResolve(baseRef.FormKey, baseRef.LinkType, out var record) ? record : null;

    private BaseShape GetBaseShape(BaseRef reference) =>
        _byBase.GetOrCreate(reference.FormKey, () => MeasureBase(reference));

    private BaseShape MeasureBase(BaseRef baseRef)
    {
        if (ResolveBaseOrNull(baseRef) is not { } record)
        {
            Interlocked.Increment(ref _basesUnresolved);
            return new BaseShape(Box.Zero, null, null);
        }

        var modelPath = record is IModeledGetter { Model: { } model } && !string.IsNullOrWhiteSpace(model.File.GivenPath)
            ? model.File.GivenPath
            : null;
        var meshWithoutGeometry = false;
        if (_useNif && modelPath != null)
        {
            var meshPath = MeshFileSource.NormalizeMeshPath(modelPath);
            var mesh = GetMeshBounds(meshPath);
            if (mesh.Box is { } nifBox)
            {
                Interlocked.Increment(ref _basesFromNif);
                return new BaseShape(nifBox, meshPath, GetStructuralInvisibleReason(record, hasModel: true, meshWithoutGeometry: false, nifBox));
            }
            meshWithoutGeometry = mesh.Status == NifReadStatus.NoRenderGeometry;
            Interlocked.Increment(ref _basesNifFallbackToObnd);
        }

        if (record is IObjectBoundedOptionalGetter { ObjectBounds: { } bounds })
        {
            Interlocked.Increment(ref _basesFromObnd);
            var box = Box.FromCorners(
                new Vector3(bounds.First.X, bounds.First.Y, bounds.First.Z),
                new Vector3(bounds.Second.X, bounds.Second.Y, bounds.Second.Z));
            return new BaseShape(box, null, GetStructuralInvisibleReason(record, modelPath != null, meshWithoutGeometry, box));
        }

        Interlocked.Increment(ref _basesWithoutBounds);
        return new BaseShape(Box.Zero, null, GetStructuralInvisibleReason(record, modelPath != null, meshWithoutGeometry, Box.Zero));
    }

    /// <summary>
    /// Record types that never render, a mesh that parsed but has no visible render geometry
    /// (marker meshes), or no mesh at all and zero-size bounds. NPCs always count as visible.
    /// </summary>
    private static string? GetStructuralInvisibleReason(IMajorRecordGetter record, bool hasModel, bool meshWithoutGeometry, Box bounds)
    {
        switch (record)
        {
            case INpcGetter: return null;
            case ILightGetter when !hasModel: return "light without mesh";
            case ISoundMarkerGetter: return "sound marker";
            case IAcousticSpaceGetter: return "acoustic space";
            case ITextureSetGetter: return "texture set (decal)";
            case IIdleMarkerGetter: return "idle marker";
        }
        if (meshWithoutGeometry) return "mesh without visible geometry (marker)";
        if (!hasModel && bounds.Size == Vector3.Zero) return "no mesh and zero bounds";
        return null;
    }

    private MeshBounds GetMeshBounds(string meshPath) =>
        _byMesh.GetOrCreate(meshPath, () => ReadMeshBounds(meshPath));

    private MeshBounds ReadMeshBounds(string meshPath)
    {
        var (bytes, source) = _meshFiles.Load(meshPath);
        CountMeshSource(source);

        var mesh = bytes == null ? NotFound(meshPath) : ParseMeshBounds(meshPath, bytes);
        if (mesh.Box.HasValue) Interlocked.Increment(ref _modelsRead);
        else Interlocked.Increment(ref _modelsFailed);
        return mesh;
    }

    private MeshBounds NotFound(string meshPath)
    {
        _messages.Add(meshPath, $"  [mesh] not found: {meshPath}");
        return new MeshBounds(null, NifReadStatus.Failed);
    }

    private MeshBounds ParseMeshBounds(string meshPath, byte[] bytes)
    {
        var status = NifGeometryReader.ReadGeometry(bytes, includeTriangles: false, out var geometry, out var error);
        if (status == NifReadStatus.Success
            && geometry != null
            && Geometry.IsWithinLimits(geometry.Min) && Geometry.IsWithinLimits(geometry.Max))
        {
            return new MeshBounds(Box.FromCorners(geometry.Min, geometry.Max), status);
        }

        _messages.Add(meshPath, $"  [mesh] unreadable: {meshPath} ({error ?? "bounds out of range"})");
        return new MeshBounds(null, status == NifReadStatus.Success ? NifReadStatus.Failed : status);
    }

    private void CountMeshSource(MeshSource source)
    {
        switch (source)
        {
            case MeshSource.LooseFile: Interlocked.Increment(ref _modelsFromLooseFiles); break;
            case MeshSource.Archive: Interlocked.Increment(ref _modelsFromArchives); break;
        }
    }
}
