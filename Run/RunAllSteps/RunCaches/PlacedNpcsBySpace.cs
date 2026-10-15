using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// The placed NPCs of each space with their possible bodies, replaced ones included, indexed per
/// space on first use. Thread-safe.
/// </summary>
internal sealed class PlacedNpcsBySpace(
    IOtherModObjectsBySpace otherModObjects,
    INpcBodies bodies,
    PlacedNpcRules rules,
    Execution execution) : IPlacedNpcsBySpace
{
    private readonly ComputedOncePerKey<RecordKey, PlacedNpcIndex> _bySpace = new(Publication.BuiltOnce, EqualityComparer<RecordKey>.Default);

    public IPlacedNpcIndex IndexOf(RecordKey space) =>
        _bySpace.Get(space, () => PlacedNpcIndex.Build(otherModObjects.ObjectsIn(space), bodies, rules, execution));
}
