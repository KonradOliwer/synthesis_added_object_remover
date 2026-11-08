using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <param name="BodiesBuilt">Bodies resolved per NPC supplying its own traits.</param>
/// <param name="BasesResolved">Placed NPC bases resolved through their templates.</param>
/// <param name="ListsResolved">Leveled lists resolved to the NPCs they can spawn.</param>
public readonly record struct ResolvedNpcBodiesStats(int BodiesBuilt, int BasesResolved, int ListsResolved);

/// <summary>The bodies of NPCs, built once each and shared by all threads. Thread-safe.</summary>
public interface INpcBodies
{
    /// <summary>Every distinct body a placed NPC base can have; null when no NPC is found through the base's templates and leveled lists.</summary>
    NpcBodySet? GetBodies(RecordKey placedBase);

    /// <summary>Counted from the cache contents.</summary>
    ResolvedNpcBodiesStats GetStats();
}
