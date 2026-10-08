using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Caches.BaseObjectShapeAndKind;

/// <summary>
/// Shape facts about base objects: the local-space bounding box (measured from the NIF mesh when
/// possible, else the base record's Object Bounds) and whether the base is invisible. Both are
/// cached per base record, failures included; the meshes' own bounds are cached by the mesh reader.
///
/// Thread-safe: each base is measured exactly once even under concurrent requests.
/// </summary>
internal sealed class BaseObjectShapes : IBaseObjectShapes
{
    private readonly IBaseFacts _bases;
    private readonly IMeshFiles _meshFiles;
    private readonly ComputedOncePerKey<RecordKey, MeasuredBase> _byBase = new(Publication.BuiltOnce, EqualityComparer<RecordKey>.Default);

    public BaseObjectShapes(IBaseFacts bases, IMeshFiles meshFiles)
    {
        _bases = bases;
        _meshFiles = meshFiles;
    }

    public IReadOnlyList<MeasuredBase> Computed() => _byBase.Contents();

    /// <summary>Counts what the caches of measured bases and mesh bounds hold.</summary>
    public static BoundsStats CountBounds(IReadOnlyList<MeasuredBase> bases, IReadOnlyList<MeshBounds> meshes, int archivesIndexed) =>
        new(
            bases.Count(measured => measured.Source == BoundsSource.MeshFile),
            bases.Count(measured => measured.MeshGaveNoBounds),
            bases.Count(measured => measured.Source == BoundsSource.ObjectBounds),
            bases.Count(measured => measured.Source == BoundsSource.None),
            bases.Count(measured => measured.Source == BoundsSource.Unresolved),
            meshes.Count(mesh => mesh.Status == MeshReadStatus.Success),
            meshes.Count(mesh => mesh.Status is not (MeshReadStatus.Success or MeshReadStatus.EffectOnly)),
            meshes.Count(mesh => mesh.Status == MeshReadStatus.EffectOnly),
            meshes.Count(mesh => mesh.Source == MeshSource.LooseFile),
            meshes.Count(mesh => mesh.Source == MeshSource.Archive),
            meshes.Count(mesh => mesh.HasFooterRoot),
            archivesIndexed,
            KeyedGroups.Rank(
                KeyedGroups.CountBy(meshes.Select(mesh => mesh.FailureKind).OfType<string>(), kind => kind, StringComparer.Ordinal),
                StringComparer.Ordinal));

    public BaseShape Of(BaseKey? baseKey) =>
        baseKey is { } key ? _byBase.Get(key.Record, () => MeasureBase(key)).Shape : BaseShape.Missing;

    public ObjectVisibility VisibilityOf(BaseKey? baseKey, bool isPrimitive, bool hasMapMarker)
    {
        if (hasMapMarker) return ObjectVisibility.Invisible(InvisibleObjectKind.MapMarkers);
        var shape = Of(baseKey);
        if (!shape.Resolved) return ObjectVisibility.MissingBase;
        if (shape.InvisibleKind is { } kind && !(isPrimitive && shape.InvisibleForLackOfGeometry)) return ObjectVisibility.Invisible(kind);
        if (shape.EffectOnlyMesh) return ObjectVisibility.EffectOnly;
        return isPrimitive && shape.MeshPath == null
            ? ObjectVisibility.Invisible(InvisibleObjectKind.TriggerBoxes)
            : ObjectVisibility.Visible;
    }

    /// <summary>NIF bounds when readable, else OBND, else none.</summary>
    private MeasuredBase MeasureBase(BaseKey baseKey)
    {
        var facts = _bases.Of(baseKey);
        if (!facts.Resolved) return new MeasuredBase(BaseShape.Missing, BoundsSource.Unresolved, MeshGaveNoBounds: false);

        var hasModel = facts.ModelPath != null;

        if (BaseObjectRules.GetMarkerFlagKind(facts) is { } markerKind)
        {
            return new MeasuredBase(new BaseShape(Box.Zero, null, markerKind), BoundsSource.Marker, MeshGaveNoBounds: false);
        }

        var meshWithoutGeometry = false;
        if (facts.ModelPath != null)
        {
            var meshPath = _meshFiles.NormalizeMeshPath(facts.ModelPath);
            var mesh = _meshFiles.Bounds.Of(meshPath);
            if (mesh.Box is { } nifBox)
            {
                return new MeasuredBase(
                    BaseObjectRules.ClassifyShape(facts, nifBox, meshPath, hasModel, meshWithoutGeometry: false), BoundsSource.MeshFile, MeshGaveNoBounds: false);
            }
            if (mesh.Status == MeshReadStatus.EffectOnly)
            {
                return new MeasuredBase(BaseObjectRules.ClassifyEffectOnlyShape(facts), BoundsSource.EffectOnlyMesh, MeshGaveNoBounds: true);
            }
            meshWithoutGeometry = mesh.Status == MeshReadStatus.NoRenderGeometry;
        }

        var meshUnreadable = hasModel;
        if (facts.ObjectBounds is { } bounds)
        {
            return new MeasuredBase(
                BaseObjectRules.ClassifyShape(facts, bounds, meshPath: null, hasModel, meshWithoutGeometry), BoundsSource.ObjectBounds, meshUnreadable);
        }

        return new MeasuredBase(
            BaseObjectRules.ClassifyShape(facts, Box.Zero, meshPath: null, hasModel, meshWithoutGeometry), BoundsSource.None, meshUnreadable);
    }
}
