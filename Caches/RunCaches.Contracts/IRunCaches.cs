namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>
/// The caches of one run, listed by their interfaces; each step receives only the ones it needs.
/// Caches belong to the run that created them and are never static.
/// <list type="bullet">
/// <item>A keyed cache builds each value once per key, because its builder may record problems or counts as a side effect; only a small pure value that depends on nothing but its key may be computed twice, the first result then being kept.</item>
/// <item>A one-piece cache is computed once, on the first ask.</item>
/// <item>There are no permanent slots. Only evictable values (triangle trees) are evicted, and what the statistics count lives in a cache that is never evicted.</item>
/// <item>Statistics are neutral counts taken from the cache contents, so they do not depend on thread count or timing.</item>
/// </list>
/// The run creates them at two points: the load-order caches (<see cref="ILoadOrderCaches"/>) at the start, and
/// these, which add the object caches (C9 to C12, <see cref="IObjectCaches"/>), once the placed objects are collected.
/// </summary>
public interface IRunCaches : ILoadOrderCaches, IObjectCaches
{
}
