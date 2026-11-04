using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <summary>How one of the cleaned mod's objects counts in the areas the area method measures.</summary>
public enum ClearedAreaObjectKind
{
    /// <summary>Drawn with a solid shape: counts by number and by box volume.</summary>
    Drawn,

    /// <summary>Its mesh holds only effect shapes (fog, light rays): counts by number alone.</summary>
    EffectOnly,

    /// <summary>A marker, sound, light or trigger box: judged by its position, counted in no share.</summary>
    Invisible,
}

/// <summary>The one place that tells which kind of object the area method treats a target as.</summary>
public static class ClearedAreaKinds
{
    /// <returns>Null for an object whose base could not be read, which the area method neither judges nor counts.</returns>
    public static ClearedAreaObjectKind? Of(IBaseObjectShapes shapes, TargetObject target) => shapes.VisibilityOf(target) switch
    {
        { Kind: not null } => ClearedAreaObjectKind.Invisible,
        { IsShown: false } => null,
        { EffectOnlyMesh: true } => ClearedAreaObjectKind.EffectOnly,
        _ => ClearedAreaObjectKind.Drawn,
    };
}
