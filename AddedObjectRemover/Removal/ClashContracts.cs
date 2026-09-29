using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <param name="Npcs">Only when NPCs clash only when stuck in an object.</param>
/// <param name="Timer">Times the sub-phases the detailed log reports.</param>
internal sealed record ClashInput(
    ImmutableArray<TargetObject> Targets,
    TargetLooks Looks,
    WorkOrder Order,
    IActiveRivals Rivals,
    INpcs? Npcs,
    ShapeCatalog Shapes,
    TriangleStore Triangles,
    Execution Exec,
    IPhaseTimer Timer);

/// <summary>The target objects too close to another mod's object.</summary>
/// <param name="Hits">In target order.</param>
/// <param name="Proposals">A removal proposal for each hit.</param>
/// <param name="Npc">Only when NPCs clash only when stuck in an object.</param>
/// <param name="LargeRivals">The rivals too large for the spatial grid in the spaces holding a visible target; zero for the BoundingBox zone.</param>
internal sealed record ClashResult(
    ImmutableArray<TooCloseHit> Hits,
    ImmutableArray<Proposal> Proposals,
    ClashWork Work,
    NpcStuckSummary? Npc,
    int LargeRivals);
