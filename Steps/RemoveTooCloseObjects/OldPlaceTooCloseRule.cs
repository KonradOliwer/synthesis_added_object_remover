using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>The run's target objects, measured where the cleaned mod put them, among the causes of the old-place world.</summary>
internal sealed class OldPlaceTooCloseRule(TooCloseObjects.OldPlaceRuleInput input) : IOldPlaceTooCloseRule
{
    public IPlacementRule Create(CollectedObjects oldPlaceWorld, OtherId leftOut)
    {
        var (caches, settings) = (input.Caches, input.Settings);
        var worldObjects = input.WorldCaches.Of(oldPlaceWorld);
        return TooCloseObjects.CreatePlacementRule(
            new(input.Targets, caches.VisibleTargets, input.Causes.Of(oldPlaceWorld, [leftOut], settings.Npcs), worldObjects.PlacedNpcs, caches.Shapes, caches.Triangles, settings));
    }
}
