using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// How far an invisible object acts, when its record says so: the reference's own radius (XRDS),
/// else its primitive box's horizontal half-size, else its base light's radius or its base sound's
/// maximum hearing distance. Thread-safe.
/// </summary>
internal sealed class InvisibleObjectReach(IBaseFacts bases)
{
    private const float HalfSizeFactor = 0.5f;

    /// <summary>Null when neither the reference nor its base defines a reach.</summary>
    public float? GetReach(TargetObject target) => target.OwnReach ?? GetBaseReach(target.Base);

    /// <summary>The reference's own reach, read while scanning; null when the reference does not define one.</summary>
    public static float? GetReferenceReach(IPlacedGetter record) => record switch
    {
        IPlacedObjectGetter { Radius: { } radius } when radius > 0 => radius,
        IPlacedObjectGetter { Primitive: { } primitive } => GetHorizontalHalfSize(primitive.Bounds),
        _ => null,
    };

    /// <summary>Mutagen exposes primitive bounds as full sizes (the record stores half-sizes).</summary>
    private static float? GetHorizontalHalfSize(P3Float bounds)
    {
        var halfSize = MathF.Max(bounds.X, bounds.Y) * HalfSizeFactor;
        return halfSize > 0 ? halfSize : null;
    }

    private float? GetBaseReach(BaseRef? baseRef)
    {
        if (baseRef is not { } reference) return null;
        var facts = bases.Of(reference);
        return facts.LightRadius ?? facts.SoundMaxDistance;
    }
}
