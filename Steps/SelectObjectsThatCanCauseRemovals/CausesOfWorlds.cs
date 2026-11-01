using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;

/// <summary>The objects that can cause removals in worlds other than the run's, selected among each world's own caches.</summary>
/// <param name="worldCaches">The object caches of the worlds asked about.</param>
internal sealed class CausesOfWorlds(IObjectCachesOfWorlds worldCaches) : ICausesOfWorlds
{
    public SelectedObjects Of(CollectedObjects world, IEnumerable<OtherId> leftOut, NpcHandling npcs) =>
        RemovalCauseSelector.SelectForOldPlace(worldCaches.Of(world).OtherModObjects, world.OtherModObjects.Length, leftOut, npcs);
}
