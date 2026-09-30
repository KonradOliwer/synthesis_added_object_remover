using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

// Narrow read-only views of the placed objects. Each view answers only what its steps may ask,
// lists ids in ascending order, and gives answers that depend only on the data, never on which
// questions were asked before. Queries that list results replace the contents of a list the
// caller owns, and queries that need scratch space take the calling worker's own buffers.

/// <summary>The visible rivals by where they are placed: only for matching replaced objects.</summary>
internal interface IRivalPositions
{
    /// <summary>Replaces <paramref name="into"/> with the visible rivals placed within <paramref name="radius"/> of <paramref name="point"/>.</summary>
    void Within(FormKey space, Vector3 point, float radius, List<OtherId> into);

    OtherObject Get(OtherId id);
}

/// <summary>The rivals that can clash with a target: visible and not replaced; placed NPCs only when they count like objects.</summary>
internal interface IActiveRivals
{
    /// <summary>Replaces <paramref name="into"/> with the active rivals whose world bounds overlap <paramref name="area"/>.</summary>
    /// <returns>How many rivals' world bounds overlap the area, active or not: a work counter.</returns>
    int Overlapping(FormKey space, Box area, SpatialQueryScratch scratch, List<OtherId> into);

    /// <summary>The lowest active rival whose mesh surrounds <paramref name="point"/>, or null.</summary>
    OtherId? FirstCovering(FormKey space, Vector3 point, SpatialQueryScratch scratch);

    OtherObject Get(OtherId id);

    /// <summary>The world-space centre of an active rival's bounds.</summary>
    Vector3 CentreOf(OtherId id);

    /// <summary>The rivals of the space too large for the grid, which every query of the space tests.</summary>
    int LargeObjectCount(FormKey space);
}

/// <summary>Other mods' placed NPCs that can spawn, with their possible bodies; replaced NPCs never match.</summary>
/// <remarks>Slots are per space, in <see cref="OtherId"/> order.</remarks>
internal interface INpcs
{
    /// <summary>Replaces <paramref name="slots"/> with the not replaced NPC slots whose body bounds may overlap <paramref name="area"/>, ascending.</summary>
    void Overlapping(FormKey space, Box area, List<int> slots);

    OtherObject NpcOf(FormKey space, int slot);

    NpcBodySet BodiesOf(FormKey space, int slot);

    /// <summary>Upright: actors turn only about Z.</summary>
    PlacedTransform TransformOf(FormKey space, int slot);

    OrientedBox WorldBoxOf(FormKey space, int slot);

    /// <summary>Finds the possible bodies of every placed NPC of the space now, unless already done.</summary>
    void MeasureBodiesIn(FormKey space);

    /// <summary>How the size of every placed NPC of the space was found, replaced ones included.</summary>
    NpcSizeCounts SizesIn(FormKey space);

    /// <summary>The NPCs of the space with a point-sized possible body, with why.</summary>
    IReadOnlyList<PointNpc> PointFallbacksIn(FormKey space);
}

/// <summary>Every visible placed object that is not a target object, replaced rivals included.</summary>
internal interface ISolids
{
    /// <summary>Replaces <paramref name="into"/> with the visible objects whose world bounds overlap <paramref name="area"/>.</summary>
    void Overlapping(FormKey space, Box area, SpatialQueryScratch scratch, List<OtherId> into);

    /// <summary>Whether the mesh of some visible object surrounds <paramref name="point"/>.</summary>
    bool Contains(FormKey space, Vector3 point, SpatialQueryScratch scratch);

    OtherObject Get(OtherId id);
}

/// <summary>A visible target object near a point: the directions it counts in and its ground footprint area.</summary>
/// <param name="Sectors">The one direction it lies in, or all of them when its box contains the point.</param>
internal readonly record struct VisibleNeighbour(TargetId Target, IReadOnlyList<DirectionSector> Sectors, float FootprintArea);

/// <summary>The visible target objects, removed or not, as oriented boxes.</summary>
internal interface IVisibleTargets
{
    /// <summary>Replaces <paramref name="into"/> with the visible targets whose box lies within <paramref name="radius"/> of <paramref name="point"/>, ascending.</summary>
    void Around(FormKey space, Vector3 point, float radius, List<VisibleNeighbour> into);

    /// <summary>Whether the mesh of some included visible target surrounds <paramref name="point"/>.</summary>
    bool AnyContains(FormKey space, Vector3 point, Func<TargetId, bool> include, SpatialQueryScratch scratch);

    /// <summary>The boxes of the included visible targets that may come within <paramref name="radius"/> of <paramref name="point"/>, and possibly a few farther ones.</summary>
    IEnumerable<OrientedBox> BoxesNear(FormKey space, Vector3 point, float radius, Func<TargetId, bool> include);
}
