using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>The object caches over worlds other than the run's, each built on its first ask with the given measurement rules. Thread-safe.</summary>
internal sealed class ObjectCachesOfWorlds(LoadOrderCaches caches, ObjectMeasurementRules rules, Execution execution) : IObjectCachesOfWorlds
{
    private readonly ComputedOncePerKey<CollectedObjects, IObjectCaches> _built = new(Publication.BuiltOnce, ReferenceEqualityComparer.Instance);

    public IObjectCaches Of(CollectedObjects world) => _built.Get(world, () => caches.AddObjectCaches(world, rules, execution));
}
