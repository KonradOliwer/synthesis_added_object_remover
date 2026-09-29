using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <param name="Hosts">The active rival each invisible target sits inside.</param>
/// <param name="Ledger">The ledger after the follow-up rounds; its removals clear the surroundings.</param>
/// <param name="Surroundings">The visible target objects, removed or not.</param>
internal sealed record LeftoverInput(
    ImmutableArray<TargetObject> Targets,
    TargetLooks Looks,
    Hosts Hosts,
    Ledger Ledger,
    IVisibleTargets Surroundings,
    IBaseFacts Bases,
    WorkOrder Order,
    Execution Exec);
