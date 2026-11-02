using System.Numerics;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>
/// How far the removal zones of the too-close rule reach from the centre of a box. It restates the growth the too-close step gives the
/// zones, without building them; a test keeps the two equal.
/// </summary>
public static class TooCloseZoneReach
{
    /// <summary>A box grown by a distance on each axis reaches this many times the distance from its centre at a corner.</summary>
    private static readonly float CornerReach = MathF.Sqrt(3f);

    /// <summary>
    /// The distance from the centre of a box of the given half extents, at any angle, within which every zone that is on lies: the
    /// larger of the sphere around the size zone and the sphere around the distance zone; 0 when no zone is on.
    /// </summary>
    public static float ReachRadius(TooCloseSettings settings, Vector3 halfExtents) =>
        MathF.Max(
            settings.BySize ? halfExtents.Length() * Boxes.SizeGrowthFactor(settings.Multiplier) : 0f,
            settings.ByDistance ? halfExtents.Length() + settings.DistanceInUnits * CornerReach : 0f);
}
