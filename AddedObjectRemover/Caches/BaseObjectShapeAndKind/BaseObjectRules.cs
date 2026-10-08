using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Caches.BaseObjectShapeAndKind;

/// <summary>The rules that decide which mesh shapes count and what kind of invisible object a base is.</summary>
internal static class BaseObjectRules
{
    /// <summary>Vanilla critter spawner activators run a script whose name starts with this.</summary>
    private const string CritterSpawnScriptPrefix = "CritterSpawn";

    /// <summary>
    /// Only solid shapes count; shapes under editor-marker or hidden nodes are skipped. When that
    /// leaves nothing but some shapes were hidden, hidden shapes count as well.
    /// </summary>
    internal static readonly ShapeInclusion SolidShapes = new(
        CountedKinds: new HashSet<MeshShapeKind> { MeshShapeKind.Solid },
        SkippedAncestorKinds: new HashSet<MeshShapeKind> { MeshShapeKind.EditorMarker },
        IncludeHidden: false,
        RetryIncludingHiddenWhenEmpty: true,
        EditorMarkerNamePart: EditorMarkerNamePart);

    /// <summary>The Creation Kit names its editor-marker shapes and nodes with this; the mesh files carry no other sign.</summary>
    private const string EditorMarkerNamePart = "EditorMarker";

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

    /// <summary>Checked before any mesh read.</summary>
    internal static InvisibleObjectKind? GetMarkerFlagKind(BaseFacts facts) => facts.MarkerFlag switch
    {
        MarkerFlagKind.XMarker => InvisibleObjectKind.XMarkers,
        MarkerFlagKind.FurnitureMarker => InvisibleObjectKind.FurnitureMarkers,
        MarkerFlagKind.DoorMarker => InvisibleObjectKind.DoorMarkers,
        MarkerFlagKind.OtherMarker => ClassifyMarker(facts),
        _ => null,
    };

    /// <summary>OBND box when present; a light whose model is only an effect (glow, light rays) stays a light.</summary>
    internal static BaseShape ClassifyEffectOnlyShape(BaseFacts facts)
    {
        var kind = facts.Kind == BaseRecordKind.Light ? InvisibleObjectKind.Lights : (InvisibleObjectKind?)null;
        return new BaseShape(facts.ObjectBounds ?? Box.Zero, null, kind, EffectOnlyMesh: true);
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

    private static InvisibleObjectKind ClassifyMarker(BaseFacts facts) =>
        facts.ScriptNames.Any(script => script.StartsWith(CritterSpawnScriptPrefix, StringComparison.OrdinalIgnoreCase))
            ? InvisibleObjectKind.CritterSpawners
            : InvisibleObjectKind.OtherMarkers;
}
