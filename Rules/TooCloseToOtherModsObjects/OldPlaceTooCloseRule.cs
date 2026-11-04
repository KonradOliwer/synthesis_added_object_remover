using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects;

/// <summary>The run's target objects, measured where the cleaned mod put them, among the causes of the old-place world.</summary>
/// <param name="worlds">The old-place worlds, whose caches are built when the first earlier version is checked.</param>
internal sealed class OldPlaceTooCloseRule(IReadOnlyList<TargetObject> targets, OtherWorlds worlds, TooCloseSettings settings) : IOldPlaceTooCloseRule
{
    public IPlacementRule Create(CollectedObjects oldPlaceWorld, OtherId leftOut)
    {
        var worldObjects = worlds.WorldCaches.Of(oldPlaceWorld);
        var caches = worlds.Caches;
        return TooCloseRules.CreatePlacementRule(
            new(targets, caches.VisibleTargets, worlds.Causes.Of(oldPlaceWorld, [leftOut], settings.Npcs), worldObjects.PlacedNpcs, caches.Shapes, caches.Triangles), settings);
    }
}
