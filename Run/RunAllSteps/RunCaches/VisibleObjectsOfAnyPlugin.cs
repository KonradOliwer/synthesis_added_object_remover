using System.Numerics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

internal sealed class VisibleObjectsOfAnyPlugin(IObjectsOfAnyPluginBySpace objectsOfAnyPlugin) : IVisibleObjectsOfAnyPlugin
{
    public void Overlapping(RecordKey space, Box area, ObjectQueryScratch scratch, List<OtherId> into)
    {
        into.Clear();
        var index = objectsOfAnyPlugin.IndexOf(space);
        index.Bounds.Overlapping(area, index.IsVisible, scratch.Spatial, scratch.Matches);
        into.AddRange(scratch.Matches.Select(slot => index[slot].Id));
    }

    public void Touching(RecordKey space, OrientedBox box, float distance, ObjectQueryScratch scratch, List<OtherId> into)
    {
        into.Clear();
        var index = objectsOfAnyPlugin.IndexOf(space);
        index.Bounds.Overlapping(
            box.WorldAabb(distance), slot => index.IsVisible(slot) && box.Intersects(index.OrientedBoxOf(slot), distance), scratch.Spatial, scratch.Matches);
        into.AddRange(scratch.Matches.Select(slot => index[slot].Id));
    }

    public bool Contains(RecordKey space, Vector3 point, ObjectQueryScratch scratch) =>
        objectsOfAnyPlugin.IndexOf(space).FindContainingVisible(point, include: _ => true, scratch) >= 0;

    public OtherObject Get(OtherId id) => objectsOfAnyPlugin.Get(id);
}
