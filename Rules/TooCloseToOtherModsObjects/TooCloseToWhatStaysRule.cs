using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects;

/// <summary>The objects to judge of a world of what stays, its target objects, measured among its other objects.</summary>
internal sealed class TooCloseToWhatStaysRule(OtherWorlds worlds) : ITooCloseToWhatStaysRule
{
    /// <summary>Covers the rounding slack of the boxes the caches index and the tolerances of the exact tests behind them.</summary>
    private const float Slack = 16f;

    public IPlacementRule Create(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings)
    {
        var caches = worlds.Caches;
        var worldObjects = worlds.WorldCaches.Of(world);
        return TooCloseRules.CreatePlacementRule(
            new(world.Targets, worldObjects.VisibleTargets, worlds.Causes.Of(world, leftOut, settings.Npcs), worldObjects.PlacedNpcs, caches.Shapes, caches.Triangles), settings);
    }

    public TooCloseClashes FindEveryClash(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings, TargetObject target) =>
        TooCloseRules.FindEveryClash(excluded => Create(world, excluded, settings), leftOut, target);

    /// <remarks>The sphere around each zone that is on, with the slack of the indexed boxes.</remarks>
    public Box ZoneReachOf(TargetObject placed, TooCloseSettings settings)
    {
        var box = OrientedBox.FromLocal(worlds.Caches.Shapes.Of(placed.Base).Box, placed.Transform);
        var radius = TooCloseZoneReach.ReachRadius(settings, box.HalfExtents) + Slack;
        return new Box(box.Center - new Vector3(radius), box.Center + new Vector3(radius));
    }

    public Box BoundsOf(OtherObject other) => OrientedBox.FromLocal(worlds.Caches.Shapes.Of(other.Base).Box, other.Transform).WorldAabb(Slack);

    public ImmutableArray<int> FindObjectsReaching(IReadOnlyList<(TargetObject Object, TooCloseSettings Settings)> objects, IReadOnlyList<OtherObject> others)
    {
        if (others.Any(other => other.IsPlacedNpc)) return [.. Enumerable.Range(0, objects.Count)];
        var bounds = new BoxesByCell<RecordKey>([.. others.Select(other => (other.SpaceKey, BoundsOf(other)))]);
        return [.. Enumerable.Range(0, objects.Count).Where(position => OverlappingIn(bounds, ZoneReachOf(objects[position])).Any())];
    }

    public ImmutableArray<int> FindOthersReached(IReadOnlyList<(TargetObject Object, TooCloseSettings Settings)> objects, IReadOnlyList<OtherObject> others)
    {
        var bounds = new BoxesByCell<RecordKey>([.. others.Select(other => (other.SpaceKey, BoundsOf(other)))]);
        var reached = objects.SelectMany(entry => OverlappingIn(bounds, ZoneReachOf(entry))).ToHashSet();
        return [.. Enumerable.Range(0, others.Count).Where(position => others[position].IsPlacedNpc || reached.Contains(position))];
    }

    private (RecordKey Space, Box Box) ZoneReachOf((TargetObject Object, TooCloseSettings Settings) entry) => (entry.Object.SpaceKey, ZoneReachOf(entry.Object, entry.Settings));

    /// <summary>The positions of the bounds of the space that overlap the box.</summary>
    private IEnumerable<int> OverlappingIn(BoxesByCell<RecordKey> bounds, (RecordKey Space, Box Box) reach) =>
        bounds.SharingACellWith(reach.Space, reach.Box).Where(position => bounds.BoxAt(position).Overlaps(reach.Box));
}
