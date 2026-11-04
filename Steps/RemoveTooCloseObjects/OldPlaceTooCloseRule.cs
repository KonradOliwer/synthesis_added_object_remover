using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>The run's target objects, measured where the cleaned mod put them, among the causes of the old-place world.</summary>
internal sealed class OldPlaceTooCloseRule(TooCloseObjects.OldPlaceRuleInput input) : IOldPlaceTooCloseRule
{
    public IPlacementRule Create(CollectedObjects oldPlaceWorld, OtherId leftOut)
    {
        var (worlds, settings) = (input.Worlds, input.Settings);
        var worldObjects = worlds.WorldCaches.Of(oldPlaceWorld);
        var caches = worlds.Caches;
        return TooCloseObjects.CreatePlacementRule(
            new(new(input.Targets, caches.VisibleTargets, worlds.Causes.Of(oldPlaceWorld, [leftOut], settings.Npcs), worldObjects.PlacedNpcs, caches.Shapes, caches.Triangles), settings));
    }
}
