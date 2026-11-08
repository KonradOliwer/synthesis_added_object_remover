using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Caches.BaseObjectShapeAndKind;

/// <summary>
/// Shape facts about base objects: the local-space bounding box (measured from the NIF mesh when
/// possible, else the base record's Object Bounds) and whether the base is invisible. Both are
/// cached per base record and link kind, failures included; the meshes' own bounds are cached by the mesh reader.
///
/// Thread-safe: each base is measured exactly once even under concurrent requests.
/// </summary>
public sealed class BaseObjectShapes : IBaseObjectShapes
{
    /// <summary>The Creation Kit names its editor-marker shapes and nodes with this; the mesh files carry no other sign.</summary>
    private const string EditorMarkerNamePart = "EditorMarker";

    /// <summary>
    /// Only solid shapes count; shapes under editor-marker or hidden nodes are skipped. When that
    /// leaves nothing but some shapes were hidden, hidden shapes count as well.
    /// </summary>
    public static readonly ShapeInclusion SolidShapes = new(
        CountedKinds: ImmutableHashSet.Create(MeshShapeKind.Solid),
        SkippedAncestorKinds: ImmutableHashSet.Create(MeshShapeKind.EditorMarker),
        IncludeHidden: false,
        RetryIncludingHiddenWhenEmpty: true,
        EditorMarkerNamePart: EditorMarkerNamePart);

    private readonly IBaseFacts _bases;
    private readonly IMeshFiles _meshFiles;
    private readonly ComputedOnce<IReadOnlyDictionary<uint, CollisionLayerFact>> _layers;
    private readonly ComputedOncePerKey<BaseKey, MeasuredBase> _byBase = new(Publication.BuiltOnce, EqualityComparer<BaseKey>.Default);

    /// <param name="readLayers">Reads the load order's collision layer records, by layer number; called once, when a layer is first asked about.</param>
    public BaseObjectShapes(IBaseFacts bases, IMeshFiles meshFiles, Func<IReadOnlyDictionary<uint, CollisionLayerFact>> readLayers)
    {
        _bases = bases;
        _meshFiles = meshFiles;
        _layers = new ComputedOnce<IReadOnlyDictionary<uint, CollisionLayerFact>>(readLayers);
    }

    public IReadOnlyList<MeasuredBase> Computed() => _byBase.Contents();

    /// <summary>Counts what the caches of measured bases and mesh bounds hold.</summary>
    public BoundsStats CountBounds()
    {
        var bases = Computed();
        var meshes = _meshFiles.Bounds.Computed();
        return new(
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
            _meshFiles.ArchivesIndexed,
            KeyedGroups.CountBy(
                meshes.Select(mesh => mesh.FailureCategory).OfType<MeshFailureCategory>(), category => category, EqualityComparer<MeshFailureCategory>.Default));
    }

    public BaseShape Of(BaseKey? baseKey) =>
        baseKey is { } key ? _byBase.Get(key, () => MeasureBase(key)).Shape : BaseShape.Missing;

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

    /// <remarks>
    /// The load order's own collision layer records decide: a layer blocks when it carries the Navmesh Obstacle flag, or its
    /// "collides with" list names the character controller's layer and it is no trigger. A number no record has is not known, so it blocks.
    /// </remarks>
    public bool LayerBlocksActors(uint collisionLayer) =>
        _layers.Value.GetValueOrDefault(collisionLayer) is not { } layer
        || layer.IsNavmeshObstacle
        || (!layer.IsTrigger && layer.CollidesWith.Contains((uint)CollisionLayer.CharController));

    private MeasuredBase MeasureBase(BaseKey baseKey)
    {
        var facts = _bases.Of(baseKey);
        var measured = MeasureShape(facts);
        return facts.Kind is BaseRecordKind.Activator or BaseRecordKind.AcousticSpace
            ? measured with { Shape = measured.Shape with { PrimitiveWithoutLayerIsWalkThrough = true } }
            : measured;
    }

    /// <summary>NIF bounds when readable, else OBND, else none.</summary>
    private MeasuredBase MeasureShape(BaseFacts facts)
    {
        if (!facts.Resolved) return new MeasuredBase(BaseShape.Missing, BoundsSource.Unresolved, MeshGaveNoBounds: false);

        var hasModel = facts.ModelPath != null;

        if (BaseObjectRules.GetMarkerFlagKind(facts) is { } markerKind)
        {
            return new MeasuredBase(new BaseShape(Box.Zero, null, markerKind), BoundsSource.Marker, MeshGaveNoBounds: false);
        }

        var meshWithoutGeometry = false;
        bool? collisionBlocksMovement = null;
        if (facts.ModelPath != null)
        {
            var meshPath = _meshFiles.NormalizeMeshPath(facts.ModelPath);
            var mesh = _meshFiles.Bounds.Of(meshPath);
            if (mesh.Box is { } nifBox)
            {
                var shape = BaseObjectRules.ClassifyShape(facts, nifBox, meshPath, hasModel, meshWithoutGeometry: false)
                    with { CollisionBlocksMovement = BaseObjectRules.BlocksMovement(mesh.CollisionLayers) };
                return new MeasuredBase(shape, BoundsSource.MeshFile, MeshGaveNoBounds: false);
            }
            if (mesh.Status == MeshReadStatus.EffectOnly)
            {
                var effectBox = mesh.EffectBox ?? throw new UnreachableException($"The effect-only mesh {meshPath} was read without the bounds of its effect shapes.");
                return new MeasuredBase(BaseObjectRules.ClassifyEffectOnlyShape(facts, effectBox), BoundsSource.EffectOnlyMesh, MeshGaveNoBounds: true);
            }
            meshWithoutGeometry = mesh.Status == MeshReadStatus.NoRenderGeometry;
            // A mesh with collision but nothing to render is an invisible wall or blocker; its collision is read all the same.
            if (meshWithoutGeometry) collisionBlocksMovement = BaseObjectRules.BlocksMovement(mesh.CollisionLayers);
        }

        var meshUnreadable = hasModel;
        if (facts.ObjectBounds is { } bounds)
        {
            return new MeasuredBase(
                BaseObjectRules.ClassifyShape(facts, bounds, meshPath: null, hasModel, meshWithoutGeometry) with { CollisionBlocksMovement = collisionBlocksMovement },
                BoundsSource.ObjectBounds,
                meshUnreadable);
        }

        return new MeasuredBase(
            BaseObjectRules.ClassifyShape(facts, Box.Zero, meshPath: null, hasModel, meshWithoutGeometry) with { CollisionBlocksMovement = collisionBlocksMovement },
            BoundsSource.None,
            meshUnreadable);
    }
}
