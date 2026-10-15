using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>How far a removal zone reaches beyond the target object: a multiple of its size, or a fixed distance in game units.</summary>
internal abstract record ZoneGrowth
{
    /// <summary>The cause the zone's hits carry.</summary>
    public abstract TooCloseCause Cause { get; }

    /// <summary>The target's local box scaled by its placement, then grown on every side: the BoundingBox zone.</summary>
    public abstract Box GrownScaledBox(Box local, float scale);

    /// <summary>The ObjectShape zone of a placed object.</summary>
    public abstract ShapeZone CreateShapeZone(Box localBox, PlacedTransform transform);

    /// <summary>The zones that are on, size zone first: a target is tested with them in this order and its first hit is its one finding.</summary>
    public static ImmutableArray<ZoneGrowth> Active(TooCloseSettings settings)
    {
        var zones = ImmutableArray.CreateBuilder<ZoneGrowth>();
        if (settings.BySize) zones.Add(new BySize(settings.Multiplier));
        if (settings.ByDistance) zones.Add(new ByDistance(settings.DistanceInUnits));
        return zones.ToImmutable();
    }

    /// <summary>The first zone that finds an other-mod object, with that object; null when none does.</summary>
    public static ZoneHit? FirstHit(ImmutableArray<ZoneGrowth> zones, Func<ZoneGrowth, OtherId?> findInZone)
    {
        foreach (var zone in zones)
        {
            if (findInZone(zone) is { } other) return new ZoneHit(other, zone.Cause);
        }
        return null;
    }

    /// <summary>
    /// Grown by a multiple of the object's size on both sides of each axis. Its shape zone is the object's own mesh, enlarged
    /// uniformly by (1 + 2 × multiplier) around the centre of its local box; other meshes must really intersect it.
    /// </summary>
    public sealed record BySize(float Multiplier) : ZoneGrowth
    {
        private const float SidesPerAxis = 2f;

        private const float IntersectionTolerance = 0f;

        public override TooCloseCause Cause { get; } = new TooCloseCause.ObjectSize();

        public override Box GrownScaledBox(Box local, float scale) => Boxes.ExpandedLocalBox(local, scale, Multiplier);

        public override ShapeZone CreateShapeZone(Box localBox, PlacedTransform transform)
        {
            var factor = 1f + SidesPerAxis * Multiplier;
            var zoneBox = OrientedBox.FromLocal(Boxes.ExpandedLocalBox(localBox, 1f, Multiplier), transform);
            return new ShapeZone(zoneBox, Boxes.EnlargeAroundCentre(transform, localBox.Center, factor), IntersectionTolerance);
        }
    }

    /// <summary>Grown by a distance in game units. Its shape zone is the object's own mesh as placed, and other meshes count when they come within the distance of it.</summary>
    public sealed record ByDistance(float Units) : ZoneGrowth
    {
        public override TooCloseCause Cause { get; } = new TooCloseCause.Distance(Units);

        public override Box GrownScaledBox(Box local, float scale) => local.Scaled(scale).Grown(Units);

        /// <remarks>The box is grown in world units, so it is scaled first and placed with scale 1.</remarks>
        public override ShapeZone CreateShapeZone(Box localBox, PlacedTransform transform) =>
            new(OrientedBox.FromLocal(GrownScaledBox(localBox, transform.Scale), transform with { Scale = 1f }), transform, Units);
    }
}
