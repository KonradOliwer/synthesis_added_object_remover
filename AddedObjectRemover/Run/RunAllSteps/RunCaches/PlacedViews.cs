using System.Numerics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

internal sealed class OtherModObjectPositions(IOtherModObjectsBySpace otherModObjects) : IOtherModObjectPositions
{
    public void Within(RecordKey space, Vector3 point, float radius, List<OtherId> into)
    {
        into.Clear();
        var index = otherModObjects.IndexOf(space);
        var slots = new List<int>();
        index.PositionGrid.CollectWithinRadius(point, radius, slot => !IsFarther(index[slot].Position, point, radius) && index.IsVisible(slot), slots);
        into.AddRange(slots.Select(slot => index[slot].Id));
    }

    /// <remarks>A NaN distance is not farther, so a broken position still counts as near.</remarks>
    private static bool IsFarther(Vector3 position, Vector3 point, float radius) => Vector3.Distance(position, point) > radius;

    public OtherObject Get(OtherId id) => otherModObjects.Get(id);
}

/// <param name="isActive">Whether an other-mod object takes part, visibility aside.</param>
internal sealed class ObjectsThatCanCauseRemovals(IOtherModObjectsBySpace otherModObjects, ObjectContainment containment, Func<OtherObject, bool> isActive)
    : IObjectsThatCanCauseRemovals
{
    public int Overlapping(RecordKey space, OrientedBox box, ObjectQueryScratch scratch, List<OtherId> into)
    {
        into.Clear();
        var index = otherModObjects.IndexOf(space);
        var candidateCount = index.Bounds.Overlapping(
            box.WorldAabb(0f), slot => IsActive(index, slot) && box.Intersects(index.OrientedBoxOf(slot), 0f), scratch.Spatial, scratch.Matches);
        into.AddRange(scratch.Matches.Select(slot => index[slot].Id));
        return candidateCount;
    }

    public OtherId? FirstCovering(RecordKey space, Vector3 point, ObjectQueryScratch scratch)
    {
        var index = otherModObjects.IndexOf(space);
        var slot = containment.FindContainingVisible(index, point, isActive, scratch);
        return slot >= 0 ? index[slot].Id : null;
    }

    public OtherId? FirstWithCentreInside(RecordKey space, Box area, Func<Vector3, bool> centreIsInside, ObjectQueryScratch scratch)
    {
        var index = otherModObjects.IndexOf(space);
        var found = index.Bounds.FirstOverlapping(area, slot => IsActive(index, slot), slot => centreIsInside(CentreOfVisible(index, slot)), scratch.Spatial);
        return found >= 0 ? index[found].Id : null;
    }

    public OtherObject Get(OtherId id) => otherModObjects.Get(id);

    public Vector3 CentreOf(OtherId id)
    {
        var (index, slot) = otherModObjects.Locate(id);
        return CentreOfVisible(index, slot);
    }

    private bool IsActive(IOtherObjectIndex index, int slot) => isActive(index[slot]) && index.IsVisible(slot);

    private static Vector3 CentreOfVisible(IOtherObjectIndex index, int slot) =>
        index.TryGetVisibleCenter(slot, out var centre)
            ? centre
            : throw new InvalidOperationException($"Other-mod object {index[slot].Key} is invisible, so it is not active.");

    public int LargeObjectCount(RecordKey space) => otherModObjects.IndexOf(space).Bounds.LargeItemCount;
}

internal sealed class NpcsThatCanSpawn(IPlacedNpcsBySpace npcs, Replacements replaced) : INpcsThatCanSpawn
{
    /// <remarks>Candidates come from the grid alone, not from <see cref="ItemsInSpace{T}"/>, whose bounds test could give a different set.</remarks>
    public void Overlapping(RecordKey space, OrientedBox box, List<int> slots)
    {
        var index = npcs.IndexOf(space);
        index.Collect(box.WorldAabb(0f), slots);
        slots.RemoveAll(slot => replaced.IsReplaced(index.NpcOf(slot).Id) || !box.Intersects(index.WorldBoxOf(slot), 0f));
    }

    public OtherObject NpcOf(RecordKey space, int slot) => npcs.IndexOf(space).NpcOf(slot);

    public NpcBodySet BodiesOf(RecordKey space, int slot) => npcs.IndexOf(space).BodiesOf(slot);

    public PlacedTransform TransformOf(RecordKey space, int slot) => npcs.IndexOf(space).TransformOf(slot);

    public OrientedBox WorldBoxOf(RecordKey space, int slot) => npcs.IndexOf(space).WorldBoxOf(slot);

    public void MeasureBodiesIn(RecordKey space) => npcs.IndexOf(space);

    public NpcSizeCounts SizesIn(RecordKey space) => npcs.IndexOf(space).Counts;

    public IReadOnlyList<PointNpc> PointFallbacksIn(RecordKey space) => npcs.IndexOf(space).PointFallbacks;
}

internal sealed class VisibleObjectsOfAnyPlugin(IObjectsOfAnyPluginBySpace objectsOfAnyPlugin, ObjectContainment containment) : IVisibleObjectsOfAnyPlugin
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
        containment.FindContainingVisible(objectsOfAnyPlugin.IndexOf(space), point, include: _ => true, scratch) >= 0;

    public OtherObject Get(OtherId id) => objectsOfAnyPlugin.Get(id);
}
