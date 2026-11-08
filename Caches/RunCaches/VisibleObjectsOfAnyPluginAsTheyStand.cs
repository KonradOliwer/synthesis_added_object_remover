using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches;

/// <summary>
/// The visible objects of any plugin with some of them moved or taken out, laid over the objects of any plugin without changing them: an unchanged
/// object is answered from them, a moved one where it stands now, and a taken-out one not at all.
/// </summary>
/// <param name="changed">Visible objects of any plugin: each where it stands now, or null when taken out.</param>
internal sealed class VisibleObjectsOfAnyPluginAsTheyStand(
    IObjectsOfAnyPluginBySpace objectsOfAnyPlugin, IReadOnlyDictionary<OtherId, OtherObject?> changed, IBaseObjectShapes shapes, ObjectContainment containment)
    : IVisibleObjectsOfAnyPlugin
{
    public void Overlapping(RecordKey space, Box area, ObjectQueryScratch scratch, List<OtherId> into) =>
        CollectVisible(
            space, area, area,
            static (_, _, _) => true,
            static (movedBox, searchBox) => movedBox.WorldAabb(0f).Overlaps(searchBox),
            scratch, into);

    public void Touching(RecordKey space, OrientedBox box, float distance, ObjectQueryScratch scratch, List<OtherId> into) =>
        CollectVisible(
            space, box.WorldAabb(distance), (box, distance),
            static (index, slot, state) => state.box.Intersects(index.OrientedBoxOf(slot), state.distance),
            static (movedBox, state) => state.box.Intersects(movedBox, state.distance),
            scratch, into);

    /// <summary>
    /// Replaces <paramref name="into"/> with the ids, ascending, of the visible objects that pass the tests: an unchanged one when its bounds overlap
    /// <paramref name="searchBox"/> and <paramref name="unchangedMatches"/> holds, a moved one when <paramref name="movedMatches"/> holds for its box where it stands now.
    /// </summary>
    private void CollectVisible<TState>(
        RecordKey space, Box searchBox, TState state, Func<IOtherObjectIndex, int, TState, bool> unchangedMatches, Func<OrientedBox, TState, bool> movedMatches,
        ObjectQueryScratch scratch, List<OtherId> into)
    {
        var index = objectsOfAnyPlugin.IndexOf(space);
        index.Bounds.Overlapping(searchBox, slot => index.IsVisible(slot) && IsUnchanged(index[slot]) && unchangedMatches(index, slot, state), scratch.Spatial, scratch.Matches);
        into.Clear();
        foreach (var slot in scratch.Matches) into.Add(index[slot].Id);
        foreach (var moved in changed.Values)
        {
            if (moved is { } standing && standing.SpaceKey == space && movedMatches(BoxOf(standing), state)) into.Add(standing.Id);
        }
        into.Sort();
    }

    public bool Contains(RecordKey space, Vector3 point, ObjectQueryScratch scratch) =>
        objectsOfAnyPlugin.IndexOf(space).FindContainingVisible(point, IsUnchanged, scratch) >= 0
        || MovedIn(space).Any(moved => containment.Contains(moved.Base, moved.Transform, point, scratch));

    public OtherObject Get(OtherId id) => changed.TryGetValue(id, out var moved) && moved is { } standing ? standing : objectsOfAnyPlugin.Get(id);

    private bool IsUnchanged(OtherObject other) => !changed.ContainsKey(other.Id);

    private IEnumerable<OtherObject> MovedIn(RecordKey space) => changed.Values.OfType<OtherObject>().Where(moved => moved.SpaceKey == space);

    private OrientedBox BoxOf(OtherObject other) => OrientedBox.FromLocal(shapes.Of(other.Base).Box, other.Transform);
}
