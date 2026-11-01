using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;

/// <summary>The too-close rule that checks an earlier version standing at its old place.</summary>
public interface IOldPlaceTooCloseRule
{
    /// <summary>
    /// The rule that checks objects standing in <paramref name="oldPlaceWorld"/>, with the objects that exist only because of the
    /// cleaned mod as its causes, except <paramref name="leftOut"/>: the cleaned mod's own version of the object checked.
    /// </summary>
    IPlacementRule Create(CollectedObjects oldPlaceWorld, OtherId leftOut);
}

/// <summary>What the old-place check of one changed object found.</summary>
/// <param name="Collision">Why the earlier version clashes at its old place; null when it stands free there or the check failed.</param>
/// <param name="Failure">Why the check stopped unexpectedly, which counts as a clash; null when it did not.</param>
public readonly record struct OldPlaceCheck(TargetId Target, RemovalReason? Collision, string? Failure);

/// <param name="WhenRemoved">What a round does with each target object it removes, by target index.</param>
/// <param name="OldPlaceChecks">One per changed object whose mode asks for the check, in target order.</param>
/// <param name="Failures">The checks that stopped unexpectedly, in target order.</param>
public sealed record ChangedObjectJudgingResult(
    ImmutableArray<WhenRemoved> WhenRemoved, ImmutableArray<OldPlaceCheck> OldPlaceChecks, ImmutableArray<PartFailure> Failures);

/// <summary>What the step judged, with the objects the old-place checks were made among.</summary>
/// <param name="OldPlaceWorld">Null when no check was made.</param>
public sealed record ChangedObjectsJudged(ChangedObjectJudgingResult Result, CollectedObjects? OldPlaceWorld);
