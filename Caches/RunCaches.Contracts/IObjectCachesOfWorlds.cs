using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>The object caches over worlds of placed objects other than the run's, each built on its first ask. Thread-safe.</summary>
public interface IObjectCachesOfWorlds
{
    /// <returns>The same caches for every ask with the same world object.</returns>
    IObjectCaches Of(CollectedObjects world);
}
