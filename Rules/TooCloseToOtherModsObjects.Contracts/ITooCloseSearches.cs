using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

/// <summary>The too-close searches over the target objects of one world, which share that world's NPCs.</summary>
public interface ITooCloseSearches
{
    /// <summary>Searches <paramref name="judged"/> with the removal zones <paramref name="settings"/> turns on; null when no zone is on.</summary>
    /// <param name="judged">The targets to search, in target order.</param>
    TooCloseFindings? Find(ImmutableArray<TargetId> judged, TooCloseSettings settings, WorkOrder order, Execution execution);
}

/// <summary>The target objects found too close with one set of settings.</summary>
/// <param name="Hits">In target order.</param>
/// <param name="Npc">Only when NPCs count only when stuck in an object, with or without the collision requirement.</param>
/// <param name="LargeOtherObjects">The other-mod objects too large for the spatial grid in the spaces holding a visible target; zero for the BoundingBox zone.</param>
/// <param name="Failures">The target objects whose check failed on the user's data, in target order; none of them is a hit.</param>
/// <param name="ZonesTested">The removal zones each target was tested with, in that order, as the cause each gives its hits.</param>
public sealed record TooCloseFindings(
    ImmutableArray<TooCloseObject> Hits,
    TooCloseWork Work,
    NpcStuckSummary? Npc,
    int LargeOtherObjects,
    ImmutableArray<PartFailure> Failures,
    ImmutableArray<TooCloseCause> ZonesTested);
