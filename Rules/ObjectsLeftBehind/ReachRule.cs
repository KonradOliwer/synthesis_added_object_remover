using System.Numerics;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>
/// How far an invisible object acts, when its record says so: the reference's own radius (XRDS),
/// else its primitive box's horizontal half-size, else its base light's radius or its base sound's
/// maximum hearing distance. Thread-safe.
/// </summary>
internal sealed class ReachRule(IBaseFacts bases) : IInvisibleObjectReach
{
    public float RadiusAround(TargetObject target, float lookAround) => GetReach(target) is { } ownReach ? MathF.Min(lookAround, ownReach) : lookAround;

    /// <summary>Null when neither the reference nor its base defines a reach.</summary>
    public float? GetReach(TargetObject target) =>
        GetReferenceReach(target.ReferenceRadius, target.PrimitiveBounds) ?? GetBaseReach(target.Base);

    /// <remarks>The primitive bounds are full sizes (the record stores half-sizes).</remarks>
    private static float? GetReferenceReach(float? referenceRadius, Vector3? primitiveBounds) => (referenceRadius, primitiveBounds) switch
    {
        ({ } radius, _) when radius > 0 => radius,
        (_, { } bounds) when Boxes.HorizontalHalfSize(bounds) is var halfSize && halfSize > 0 => halfSize,
        _ => null,
    };

    private float? GetBaseReach(BaseKey? baseKey)
    {
        if (baseKey is not { } key) return null;
        var facts = bases.Of(key);
        return facts.LightRadius ?? facts.SoundMaxDistance;
    }
}
