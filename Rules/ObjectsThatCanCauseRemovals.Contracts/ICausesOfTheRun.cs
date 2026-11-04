using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;

/// <summary>The objects that can cause removals in the run's world: every visible other mod's object a target does not replace.</summary>
public interface ICausesOfTheRun
{
    /// <param name="otherModObjects">The objects added by other mods.</param>
    /// <param name="npcs">The NPC handling of the settings the objects are selected for.</param>
    SelectedObjects Select(CollectedObjects world, IOtherModObjectsBySpace otherModObjects, NpcHandling npcs);
}
