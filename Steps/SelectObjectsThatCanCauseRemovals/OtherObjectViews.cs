using System.Numerics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;

internal sealed class OtherModObjectPositions(IOtherModObjectsBySpace otherModObjects) : IOtherModObjectPositions
{
    public void Within(RecordKey space, Vector3 point, float radius, List<OtherId> into)
    {
        into.Clear();
        var index = otherModObjects.IndexOf(space);
        var slots = new List<int>();
        index.PositionGrid.CollectWithinRadius(point, radius, slot => !Vectors.IsFartherThan(index[slot].Position, point, radius) && index.IsVisible(slot), slots);
        into.AddRange(slots.Select(slot => index[slot].Id));
    }

    public OtherObject Get(OtherId id) => otherModObjects.Get(id);
}

/// <summary>The visible other-mod objects that are not replaced; placed NPCs only when <paramref name="npcs"/> counts them like objects.</summary>
internal sealed class ObjectsThatCanCauseRemovals(IOtherModObjectsBySpace otherModObjects, Replacements replaced, NpcHandling npcs)
    : IObjectsThatCanCauseRemovals
{
    public int Overlapping(RecordKey space, OrientedBox box, ObjectQueryScratch scratch, List<OtherId> into)
    {
        into.Clear();
        var index = otherModObjects.IndexOf(space);
        var candidateCount = index.Bounds.Overlapping(box.WorldAabb(0f), slot => PassesBoxFilter(index, slot, box), scratch.Spatial, scratch.Matches);
        into.AddRange(scratch.Matches.Select(slot => index[slot].Id));
        return candidateCount;
    }

    public bool Overlaps(OtherId id, OrientedBox box)
    {
        var (index, slot) = otherModObjects.Locate(id);
        return index.Bounds.Overlaps(slot, box.WorldAabb(0f)) && PassesBoxFilter(index, slot, box);
    }

    private bool PassesBoxFilter(IOtherObjectIndex index, int slot, OrientedBox box) => IsActive(index, slot) && box.Intersects(index.OrientedBoxOf(slot), 0f);

    public OtherId? FirstCovering(RecordKey space, Vector3 point, Func<OtherId, bool> include, ObjectQueryScratch scratch)
    {
        var index = otherModObjects.IndexOf(space);
        var slot = index.FindContainingVisible(point, other => CanCauseRemovals(other) && include(other.Id), scratch);
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

    private bool CanCauseRemovals(OtherObject other) => !replaced.IsReplaced(other.Id) && (npcs == NpcHandling.AlwaysLikeAnyObject || !other.IsPlacedNpc);

    private bool IsActive(IOtherObjectIndex index, int slot) => CanCauseRemovals(index[slot]) && index.IsVisible(slot);

    private static Vector3 CentreOfVisible(IOtherObjectIndex index, int slot) =>
        index.TryGetVisibleCenter(slot, out var centre)
            ? centre
            : throw new InvalidOperationException($"Other-mod object {index[slot].Key} is invisible, so it is not active.");

    public int LargeObjectCount(RecordKey space) => otherModObjects.IndexOf(space).Bounds.LargeItemCount;
}
