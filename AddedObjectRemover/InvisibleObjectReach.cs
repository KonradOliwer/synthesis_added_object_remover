using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// How far an invisible object acts, when its record says so: the reference's own radius (XRDS),
/// else its primitive box's horizontal half-size, else its base light's radius or its base sound's
/// maximum hearing distance. Thread-safe; base reaches are cached per base.
/// </summary>
internal sealed class InvisibleObjectReach(ILinkCache linkCache, BaseObjectShapeProvider shapes)
{
    private const float HalfSizeFactor = 0.5f;

    private readonly LazyCache<FormKey, float?> _byBase = new();

    /// <summary>Null when neither the reference nor its base defines a reach.</summary>
    public float? GetReach(TargetObject target) => GetReferenceReach(target.Record) ?? GetBaseReach(target.Base);

    private static float? GetReferenceReach(IPlacedGetter record) => record switch
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

    private float? GetBaseReach(BaseRef? baseRef) =>
        baseRef is { } reference ? _byBase.GetOrCreate(reference.FormKey, () => MeasureBaseReach(reference)) : null;

    private float? MeasureBaseReach(BaseRef reference) => shapes.ResolveBaseOrNull(reference) switch
    {
        ILightGetter { Radius: > 0 } light => light.Radius,
        ISoundMarkerGetter marker => GetMaxHearingDistance(marker),
        _ => null,
    };

    private float? GetMaxHearingDistance(ISoundMarkerGetter marker) =>
        marker.SoundDescriptor.TryResolve(linkCache, out var descriptor)
        && descriptor.OutputModel.TryResolve(linkCache, out var outputModel)
        && outputModel.Attenuation is { MaxDistance: > 0 } attenuation
            ? attenuation.MaxDistance
            : null;
}
