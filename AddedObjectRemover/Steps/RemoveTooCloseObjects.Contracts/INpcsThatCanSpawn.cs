using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>Other mods' placed NPCs that can spawn, with their possible bodies; replaced NPCs never match.</summary>
/// <remarks>Slots are per space, in <see cref="OtherId"/> order. Queries that list results replace the contents of a list the caller owns.</remarks>
public interface INpcsThatCanSpawn
{
    /// <summary>Replaces <paramref name="slots"/> with the not replaced NPC slots whose body box overlaps <paramref name="box"/>, ascending.</summary>
    void Overlapping(RecordKey space, OrientedBox box, List<int> slots);

    OtherObject NpcOf(RecordKey space, int slot);

    NpcBodySet BodiesOf(RecordKey space, int slot);

    /// <summary>Upright: actors turn only about Z.</summary>
    PlacedTransform TransformOf(RecordKey space, int slot);

    OrientedBox WorldBoxOf(RecordKey space, int slot);

    /// <summary>Finds the possible bodies of every placed NPC of the space now, unless already done.</summary>
    void MeasureBodiesIn(RecordKey space);

    /// <summary>How the size of every placed NPC of the space was found, replaced ones included.</summary>
    NpcSizeCounts SizesIn(RecordKey space);

    /// <summary>The NPCs of the space with a point-sized possible body, with why.</summary>
    IReadOnlyList<PointNpc> PointFallbacksIn(RecordKey space);
}
