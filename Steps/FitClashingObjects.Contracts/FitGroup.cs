using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.FitClashingObjects.Contracts;

/// <summary>Target objects that are tried shrunk or turned within one settings record, against the collision rules of their own.</summary>
/// <param name="Judged">The objects in target order, with the limits for shrinking or turning them.</param>
/// <param name="Fittable">The rule whose clash an object may escape by being shrunk or turned.</param>
/// <param name="Unfittable">
/// The rule whose clash is never fitted: an object that clashes by it where it stands is left to its removal round, and a shrunk or
/// turned placement must not clash by it either.
/// </param>
public sealed record FitGroup(ObjectsJudgedWith<FitSettings> Judged, IPlacementRule Fittable, IPlacementRule Unfittable);
