using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>The placed NPCs of C10 without the replaced ones, which cannot spawn.</summary>
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
