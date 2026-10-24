using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// The visible objects of any plugin with some of them moved or taken out, laid over C11 without changing it: an unchanged
/// object is answered from C11, a moved one where it stands now, and a taken-out one not at all.
/// </summary>
/// <param name="changed">Visible objects of C11: each where it stands now, or null when taken out.</param>
internal sealed class VisibleObjectsOfAnyPluginAsTheyStand(
    IObjectsOfAnyPluginBySpace objectsOfAnyPlugin, IReadOnlyDictionary<OtherId, OtherObject?> changed, IBaseObjectShapes shapes, ObjectContainment containment)
    : IVisibleObjectsOfAnyPlugin
{
    public void Overlapping(RecordKey space, Box area, ObjectQueryScratch scratch, List<OtherId> into)
    {
        var index = objectsOfAnyPlugin.IndexOf(space);
        index.Bounds.Overlapping(area, slot => index.IsVisible(slot) && IsUnchanged(index[slot]), scratch.Spatial, scratch.Matches);
        Collect(scratch.Matches.Select(slot => index[slot].Id), MovedIn(space).Where(moved => BoxOf(moved).WorldAabb(0f).Overlaps(area)), into);
    }

    public void Touching(RecordKey space, OrientedBox box, float distance, ObjectQueryScratch scratch, List<OtherId> into)
    {
        var index = objectsOfAnyPlugin.IndexOf(space);
        index.Bounds.Overlapping(
            box.WorldAabb(distance), slot => index.IsVisible(slot) && IsUnchanged(index[slot]) && box.Intersects(index.OrientedBoxOf(slot), distance), scratch.Spatial, scratch.Matches);
        Collect(scratch.Matches.Select(slot => index[slot].Id), MovedIn(space).Where(moved => box.Intersects(BoxOf(moved), distance)), into);
    }

    public bool Contains(RecordKey space, Vector3 point, ObjectQueryScratch scratch) =>
        objectsOfAnyPlugin.IndexOf(space).FindContainingVisible(point, IsUnchanged, scratch) >= 0
        || MovedIn(space).Any(moved => containment.Contains(moved.Base, moved.Transform, point, scratch));

    public OtherObject Get(OtherId id) => changed.TryGetValue(id, out var moved) && moved is { } standing ? standing : objectsOfAnyPlugin.Get(id);

    private bool IsUnchanged(OtherObject other) => !changed.ContainsKey(other.Id);

    private IEnumerable<OtherObject> MovedIn(RecordKey space) => changed.Values.OfType<OtherObject>().Where(moved => moved.SpaceKey == space);

    private OrientedBox BoxOf(OtherObject other) => OrientedBox.FromLocal(shapes.Of(other.Base).Box, other.Transform);

    /// <summary>Replaces <paramref name="into"/> with both lists' ids in ascending order.</summary>
    private static void Collect(IEnumerable<OtherId> unchanged, IEnumerable<OtherObject> moved, List<OtherId> into)
    {
        List<OtherId> found = [.. unchanged, .. moved.Select(other => other.Id)];
        into.Clear();
        into.AddRange(found.OrderBy(id => id.Index));
    }
}
