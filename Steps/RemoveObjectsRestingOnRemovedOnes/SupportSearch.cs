using AddedObjectRemover.Caches.RunCaches.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>The touch search over every target object and the contact finder over it, which measure an object's support.</summary>
internal sealed record SupportSearch(TouchSearch Search, SupportContactFinder Contacts)
{
    /// <param name="askObjectsOfAnyPlugin">Gives the objects of any plugin that can support a target object; asked once the touch search is built.</param>
    /// <param name="touchGap">The largest gap that counts as touching.</param>
    public static SupportSearch Create(SupportReads reads, Func<IVisibleObjectsOfAnyPlugin> askObjectsOfAnyPlugin, float touchGap)
    {
        var search = TouchSearch.Create(reads.Targets, excluded: [], reads.Shapes, reads.Triangles, touchGap, reads.Exec);
        var supporterFinder = new SupporterFinder(reads.Targets, search, askObjectsOfAnyPlugin(), reads.Shapes, touchGap);
        var contacts = new SupportContactFinder(reads.Targets, search.MeshPaths, supporterFinder, new SurfaceContacts(reads.Terrain, search.Cache, reads.Surfaces), touchGap);
        return new SupportSearch(search, contacts);
    }
}
