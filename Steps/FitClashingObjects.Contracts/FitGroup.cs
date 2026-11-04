using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.FitClashingObjects.Contracts;

/// <summary>Target objects that are tried shrunk or turned within one settings record, against the collision rules of their own.</summary>
/// <param name="Judged">The objects in target order, with the limits for shrinking or turning them.</param>
/// <param name="Fittable">The rule whose clash an object may escape by being shrunk or turned.</param>
/// <param name="Unfittable">
/// The rule whose clash is never fitted: an object that clashes by it where it stands is left to its removal round, and a shrunk or
/// turned placement must not clash by it either.
/// </param>
/// <remarks>
/// The too-close rule is the fittable one and the changed-terrain rule the unfittable one: shrinking or turning an object does not
/// change the ground under it, so an object on changed terrain is removed as it stands.
/// </remarks>
public sealed record FitGroup(ObjectsJudgedWith<FitSettings> Judged, IPlacementRule Fittable, IPlacementRule Unfittable);
