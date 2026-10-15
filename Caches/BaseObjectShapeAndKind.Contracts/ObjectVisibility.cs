namespace AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

/// <summary>Whether a placed object can be seen in game, and if not, what kind of invisible object it is.</summary>
/// <param name="Kind">Null for a visible object, one whose base was not found, or one with an effect-only mesh.</param>
/// <param name="BaseMissing">The base object is not set or not in the load order, so the game shows nothing.</param>
/// <param name="EffectOnlyMesh">The mesh holds only effect-shader shapes (fog, light rays, water spray), nothing solid.</param>
public readonly record struct ObjectVisibility(InvisibleObjectKind? Kind, bool BaseMissing, bool EffectOnlyMesh = false)
{
    public static ObjectVisibility Visible => new(null, false);

    public static ObjectVisibility MissingBase => new(null, true);

    public static ObjectVisibility EffectOnly => new(null, false, EffectOnlyMesh: true);

    public static ObjectVisibility Invisible(InvisibleObjectKind kind) => new(kind, false);

    public bool IsVisible => Kind == null && !BaseMissing && !EffectOnlyMesh;
}
