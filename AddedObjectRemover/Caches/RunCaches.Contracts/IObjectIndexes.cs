using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>
/// The other-mod objects of one space, found by their raw position (<see cref="PositionGrid"/>) and
/// by their world bounds (<see cref="Bounds"/>). Thread-safe.
/// </summary>
public interface IOtherObjectIndex
{
    /// <summary>The objects by raw position: only for matching objects by where they are placed.</summary>
    GridCandidates PositionGrid { get; }

    /// <summary>The objects by world bounds: for every question about what an object's bounds reach.</summary>
    ItemsInSpace<OtherObject> Bounds { get; }

    OtherObject this[int index] { get; }

    OrientedBox OrientedBoxOf(int index);

    /// <summary>World-space bounds center of a visible object; false (and no center) for an invisible one.</summary>
    bool TryGetVisibleCenter(int index, out Vector3 center);

    bool IsVisible(int index);
}

/// <summary>The placed NPCs of one space that can spawn, with their bodies; slots are in <see cref="OtherId"/> order. Thread-safe.</summary>
public interface IPlacedNpcIndex
{
    NpcSizeCounts Counts { get; }

    /// <summary>For the detailed log only: the NPCs with a point-sized possible body, with why its real size is unknown.</summary>
    IReadOnlyList<PointNpc> PointFallbacks { get; }

    OtherObject NpcOf(int slot);

    NpcBodySet BodiesOf(int slot);

    PlacedTransform TransformOf(int slot);

    OrientedBox WorldBoxOf(int slot);

    /// <summary>Replaces <paramref name="slots"/> with the sorted, distinct NPC slots whose body AABB may overlap <paramref name="area"/>.</summary>
    void Collect(Box area, List<int> slots);
}
