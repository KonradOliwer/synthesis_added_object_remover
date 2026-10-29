using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>The target objects too close to another mod's object, found with one set of settings.</summary>
/// <param name="Objects">The target objects the pass judged, in target order.</param>
/// <param name="Outcome"><see cref="StepOutcome.SwitchedOff"/> when no removal zone is on; the pass then holds no hits and no work.</param>
/// <param name="Hits">In target order.</param>
/// <param name="Proposals">A removal proposal for each hit.</param>
/// <param name="Npc">Only when NPCs count only when stuck in an object, with or without the collision requirement.</param>
/// <param name="LargeOtherObjects">The other-mod objects too large for the spatial grid in the spaces holding a visible target; zero for the BoundingBox zone.</param>
/// <param name="Failures">The target objects whose check failed unexpectedly, in target order; none of them is a hit.</param>
/// <param name="ZonesTested">The removal zones each target was tested with, in that order, as the cause each gives its hits; empty when switched off.</param>
/// <param name="Zone">The removal zone the pass used.</param>
/// <param name="Npcs">How the pass handled other mods' placed NPCs; <paramref name="Npc"/> is present exactly for <see cref="NpcHandling.OnlyWhenStuckInsideTheObject"/>
/// and <see cref="NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision"/>.</param>
public sealed record TooClosePass(
    ImmutableArray<TargetId> Objects,
    StepOutcome Outcome,
    ImmutableArray<TooCloseObject> Hits,
    ImmutableArray<ProposedRemoval> Proposals,
    TooCloseWork Work,
    NpcStuckSummary? Npc,
    int LargeOtherObjects,
    ImmutableArray<PartFailure> Failures,
    ImmutableArray<TooCloseCause> ZonesTested,
    ZoneShape Zone,
    NpcHandling Npcs) : IJudgedPass;

/// <summary>The target objects too close to another mod's object.</summary>
public sealed record TooCloseResult(ImmutableArray<TooClosePass> Passes) : JudgedPasses<TooClosePass>(Passes);
