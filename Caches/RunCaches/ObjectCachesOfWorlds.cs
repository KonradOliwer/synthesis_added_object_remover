using System.Runtime.CompilerServices;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches;

/// <summary>
/// The object caches over worlds other than the run's, each built on its first ask with the given measurement rules and kept only
/// while its world is in use, so the worlds a step builds again and again do not pile up. Thread-safe.
/// </summary>
internal sealed class ObjectCachesOfWorlds(LoadOrderCaches caches, ObjectMeasurementRules rules, Execution execution) : IObjectCachesOfWorlds
{
    private readonly ConditionalWeakTable<CollectedObjects, Lazy<IObjectCaches>> _built = new();

    public IObjectCaches Of(CollectedObjects world) =>
        _built.GetValue(world, key => new Lazy<IObjectCaches>(() => caches.AddObjectCaches(key, rules, execution), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
}
