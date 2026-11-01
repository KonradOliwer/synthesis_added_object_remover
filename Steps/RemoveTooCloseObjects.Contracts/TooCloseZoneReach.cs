using System.Numerics;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>
/// How far the removal zones of the too-close rule reach beyond a target object, so that a step which only needs to know whether
/// another object can meet a zone (not which zone) uses the rule's own growth.
/// </summary>
public static class TooCloseZoneReach
{
    private const float SidesPerAxis = 2f;

    /// <summary>A box grown by a distance on each axis reaches this many times the distance from its centre at a corner.</summary>
    private static readonly float CornerReach = MathF.Sqrt(3f);

    /// <summary>The placement of the target's mesh enlarged uniformly around the centre of its local box by the size zone: its size plus the multiplier of it on both sides of each axis.</summary>
    public static PlacedTransform EnlargeBySize(PlacedTransform transform, Vector3 localCentre, float multiplier) =>
        Boxes.EnlargeAroundCentre(transform, localCentre, SizeEnlargement(multiplier));

    /// <summary>
    /// The distance from the centre of a box of the given half extents, at any angle, within which every zone that is on lies: the
    /// larger of the sphere around the size zone and the sphere around the distance zone; 0 when no zone is on.
    /// </summary>
    public static float ReachRadius(TooCloseSettings settings, Vector3 halfExtents)
    {
        var corner = halfExtents.Length();
        var bySize = settings.BySize ? corner * SizeEnlargement(settings.Multiplier) : 0f;
        var byDistance = settings.ByDistance ? corner + settings.DistanceInUnits * CornerReach : 0f;
        return MathF.Max(bySize, byDistance);
    }

    private static float SizeEnlargement(float multiplier) => 1f + SidesPerAxis * multiplier;
}
