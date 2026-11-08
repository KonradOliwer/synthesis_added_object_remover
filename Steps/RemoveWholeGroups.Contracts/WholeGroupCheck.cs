using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

/// <summary>One standing object of a seeded group, judged by the step; the round join sets the keep reason of a referenced object.</summary>
/// <param name="GroupIndex">Its group in <see cref="WholeGroupsResult.Groups"/>.</param>
public sealed record WholeGroupCheck(int TargetIndex, int GroupIndex, LeftBehindOutcome Decision)
    : LeftBehindVerdict(TargetIndex, Decision, KeepReason: null);
