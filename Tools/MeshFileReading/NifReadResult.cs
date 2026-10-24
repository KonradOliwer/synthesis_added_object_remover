using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <param name="Geometry">Set only on success.</param>
/// <param name="Failure">Why the read did not succeed; null on success.</param>
/// <param name="MismatchedStrips">NiTriStrips shapes of a successful read whose strip lengths do not match their points.</param>
internal sealed record NifReadResult(
    MeshReadStatus Status,
    NifGeometry? Geometry,
    MeshReadFailure? Failure,
    int MismatchedStrips)
{
    /// <summary>Set when the file footer's root node was used instead of NiflySharp's GetRootNode().</summary>
    public NifRoot? FooterRoot { get; init; }

    /// <summary>Set when a NiTriStrips shape needed strip fields this NiflySharp version lacks.</summary>
    public bool StripFieldsMissing { get; init; }

    /// <summary>The distinct layers of the collision rigid bodies, in ascending order; null when the file did not load or has no root node.</summary>
    public ImmutableArray<CollisionLayer>? CollisionLayers { get; init; }

    public static NifReadResult Succeeded(NifGeometry geometry, int mismatchedStrips) =>
        new(MeshReadStatus.Success, geometry, null, mismatchedStrips);

    public static NifReadResult Failed(MeshReadFailure failure) =>
        new(MeshReadStatus.Failed, null, failure, 0);

    public static NifReadResult WithoutRenderGeometry(ShapeCounts shapes) =>
        new(MeshReadStatus.NoRenderGeometry, null, MeshReadFailure.NoRenderGeometry(shapes), 0);

    public static NifReadResult WithEffectsOnly(ShapeCounts shapes) =>
        new(MeshReadStatus.EffectOnly, null, MeshReadFailure.EffectOnly(shapes), 0);
}
