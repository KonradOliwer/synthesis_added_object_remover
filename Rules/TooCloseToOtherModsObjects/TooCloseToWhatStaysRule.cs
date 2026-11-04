using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects;

/// <summary>The candidates of a world of what stays, its target objects, measured among its other objects.</summary>
internal sealed class TooCloseToWhatStaysRule(OtherWorlds worlds) : ITooCloseToWhatStaysRule
{
    public IPlacementRule Create(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings)
    {
        var caches = worlds.Caches;
        var worldObjects = worlds.WorldCaches.Of(world);
        return TooCloseRules.CreatePlacementRule(
            new(world.Targets, worldObjects.VisibleTargets, worlds.Causes.Of(world, leftOut, settings.Npcs), worldObjects.PlacedNpcs, caches.Shapes, caches.Triangles), settings);
    }
}
