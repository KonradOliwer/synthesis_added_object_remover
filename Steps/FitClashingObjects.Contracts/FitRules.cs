using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.FitClashingObjects.Contracts;

/// <summary>The removal steps' rules the fit step asks.</summary>
/// <param name="Fittable">The rule whose clash an object may escape by being shrunk or turned.</param>
/// <param name="Unfittable">
/// The rule whose clash is never fitted: an object that clashes by it where it stands is left to its removal round, and a shrunk or
/// turned placement must not clash by it either.
/// </param>
/// <param name="Resting">Names the objects others rest on, which are left to the removal rounds.</param>
public sealed record FitRules(IPlacementRule Fittable, IPlacementRule Unfittable, IRestingRule Resting);
