using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.FitClashingObjects.Contracts;

/// <summary>What trying the clashing objects shrunk or turned where they stand found.</summary>
/// <param name="Fits">The objects that fit shrunk or turned, in target order.</param>
/// <param name="Clashing">The objects that may be shrunk or turned and clash by the fittable rule only, where they stand.</param>
/// <param name="RestedOn">Those of <paramref name="Clashing"/> that other objects rest on, which are not tried.</param>
/// <param name="Tried">Those of <paramref name="Clashing"/> tried shrunk or turned.</param>
/// <param name="Failures">For each tried object whose check failed at a placement, the first such failure, in target order; or the failure that stopped the search for the objects others rest on.</param>
public sealed record FitResult(ImmutableArray<ProposedFit> Fits, int Clashing, int RestedOn, int Tried, ImmutableArray<PartFailure> Failures);
