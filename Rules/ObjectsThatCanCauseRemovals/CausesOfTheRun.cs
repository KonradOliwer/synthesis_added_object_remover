using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals;

/// <summary>The objects that can cause removals in the run's world: the other mods' objects no target replaces.</summary>
internal sealed class CausesOfTheRun(IBaseObjectShapes shapes, Execution execution) : ICausesOfTheRun
{
    public SelectedObjects Select(CollectedObjects world, IOtherModObjectsBySpace otherModObjects, NpcHandling npcs)
    {
        var replacements = ReplacementMatcher.Find(
            world.Targets, new OtherModObjectPositions(otherModObjects), world.OtherModObjects.Length, shapes, world.Order, execution);
        return RemovalCauses.Selected(otherModObjects, replacements, npcs);
    }
}
