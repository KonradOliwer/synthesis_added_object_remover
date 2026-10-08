using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>The target objects too close to another mod's object.</summary>
/// <param name="Hits">In target order.</param>
/// <param name="Proposals">A removal proposal for each hit.</param>
/// <param name="Npc">Only when NPCs count only when stuck in an object.</param>
/// <param name="LargeOtherObjects">The other-mod objects too large for the spatial grid in the spaces holding a visible target; zero for the BoundingBox zone.</param>
/// <param name="Failures">The target objects whose check failed unexpectedly, in target order; none of them is a hit.</param>
public sealed record TooCloseResult(
    ImmutableArray<TooCloseObject> Hits,
    ImmutableArray<ProposedRemoval> Proposals,
    TooCloseWork Work,
    NpcStuckSummary? Npc,
    int LargeOtherObjects,
    ImmutableArray<TargetFailure> Failures);
