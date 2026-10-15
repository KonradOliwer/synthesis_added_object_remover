using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

/// <summary>
/// How far an invisible object acts, when its record says so: the reference's own radius (XRDS),
/// else its primitive box's horizontal half-size, else its base light's radius or its base sound's
/// maximum hearing distance. Thread-safe.
/// </summary>
internal sealed class ReachRule(IBaseFacts bases)
{
    /// <summary>Null when neither the reference nor its base defines a reach.</summary>
    public float? GetReach(TargetObject target) =>
        GetReferenceReach(target.ReferenceRadius, target.PrimitiveBounds) ?? GetBaseReach(target.Base);

    private static float? GetReferenceReach(float? referenceRadius, Vector3? primitiveBounds) => (referenceRadius, primitiveBounds) switch
    {
        ({ } radius, _) when radius > 0 => radius,
        (_, { } bounds) => GetPositiveHalfSize(bounds),
        _ => null,
    };

    /// <summary>The primitive bounds are full sizes (the record stores half-sizes).</summary>
    private static float? GetPositiveHalfSize(Vector3 bounds)
    {
        var halfSize = Boxes.HorizontalHalfSize(bounds);
        return halfSize > 0 ? halfSize : null;
    }

    private float? GetBaseReach(BaseKey? baseKey)
    {
        if (baseKey is not { } key) return null;
        var facts = bases.Of(key);
        return facts.LightRadius ?? facts.SoundMaxDistance;
    }
}
