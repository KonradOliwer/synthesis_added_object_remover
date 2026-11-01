using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>The objects that can cause removals in worlds of placed objects other than the run's, which the step's rules for those worlds judge among.</summary>
public interface ICausesOfWorlds
{
    /// <summary>Every other mod's object of <paramref name="world"/> can cause removals except <paramref name="leftOut"/>.</summary>
    SelectedObjects Of(CollectedObjects world, IEnumerable<OtherId> leftOut, NpcHandling npcs);
}
