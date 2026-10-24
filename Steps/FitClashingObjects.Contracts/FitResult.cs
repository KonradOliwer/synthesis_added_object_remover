using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.FitClashingObjects.Contracts;

/// <summary>What trying the clashing objects at other placements found.</summary>
/// <param name="Fits">The objects that fit at another placement, in target order.</param>
/// <param name="Tried">The objects that clashed where they stood and were tried at other placements.</param>
/// <param name="Failures">For each tried object whose check failed at a placement, the first such failure, in target order.</param>
public sealed record FitResult(ImmutableArray<ProposedFit> Fits, int Tried, ImmutableArray<PartFailure> Failures);
