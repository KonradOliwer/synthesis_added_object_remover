using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>The candidates of a world of what stays, its target objects, measured among its other objects.</summary>
internal sealed class TooCloseToWhatStaysRule(TooCloseObjects.WorldRuleInput input) : ITooCloseToWhatStaysRule
{
    public IPlacementRule Create(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings)
    {
        var caches = input.Caches;
        var worldObjects = input.WorldCaches.Of(world);
        return TooCloseObjects.CreatePlacementRule(
            new(new(world.Targets, worldObjects.VisibleTargets, input.Causes.Of(world, leftOut, settings.Npcs), worldObjects.PlacedNpcs, caches.Shapes, caches.Triangles), settings));
    }
}
