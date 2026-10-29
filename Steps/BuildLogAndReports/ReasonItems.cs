using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The comma-separated count items of a removal or restore summary, one per reason, for the objects it counts.</summary>
internal static class ReasonItems
{
    /// <summary>The items' words that depend on what became of the objects the summary counts.</summary>
    /// <param name="FollowUp">The item for the objects a follow-up round took from their place.</param>
    /// <param name="Linked">The item for the objects that were members of a linked group.</param>
    /// <param name="LinkedToRestored">
    /// The item, printed only when it counts any, for the members of a linked group that followed a restored object, which
    /// <paramref name="Linked"/> then leaves out; null when <paramref name="Linked"/> counts every member.
    /// </param>
    internal sealed record Wording(string FollowUp, string Linked, string? LinkedToRestored);

    public static readonly Wording ForRemovals = new("follow-up removals", "linked to removed objects", "linked to restored objects");

    public static readonly Wording ForRestorations = new("follow-up", "linked", LinkedToRestored: null);

    /// <param name="objects">The objects the summary counts.</param>
    /// <param name="final">The run's decisions, which tell the follow-up removals that were first shrunk or turned.</param>
    /// <param name="changedTerrain">The changed-terrain step's outcome; its item only shows when the step was performed.</param>
    /// <param name="visibleLeftBehind">The visible left-behind step's outcome; its item only shows when the step was performed.</param>
    /// <param name="wording">The words for the follow-up and linked items.</param>
    public static string Describe(
        IReadOnlyList<ObjectWithReason> objects, IRemovalDecisions final, StepOutcome changedTerrain, StepOutcome visibleLeftBehind, Wording wording)
    {
        List<ObjectWithReason> followUps = [.. objects.Where(IsFollowUp)];
        var linkedToRestored = wording.LinkedToRestored is null ? 0 : objects.OfType<ObjectWithReason.LinkedTo>().Count(entry => final.Of(new TargetId(entry.LinkedToTargetIndex)) is Decision.Restored);
        return $"{ItemWhenPerformed<ObjectWithReason.TerrainChanged>(objects, changedTerrain, "on changed terrain")}"
            + $"{TextFormat.Count(Count<ObjectWithReason.TooClose>(objects))} too close, "
            + $"{TextFormat.Count(followUps.Count)} {wording.FollowUp}{FirstShrunkOrTurned(followUps, final)}, "
            + $"{ItemWhenPerformed<ObjectWithReason.VisibleLeftBehind>(objects, visibleLeftBehind, "visible objects left behind")}"
            + $"{TextFormat.Count(Count<ObjectWithReason.LeftBehind>(objects))} invisible objects left behind, "
            + $"{TextFormat.Count(Count<ObjectWithReason.LinkedTo>(objects) - linkedToRestored)} {wording.Linked}"
            + (linkedToRestored > 0 ? $", {TextFormat.Count(linkedToRestored)} {wording.LinkedToRestored}" : string.Empty);
    }

    private static int Count<TObject>(IEnumerable<ObjectWithReason> objects) where TObject : ObjectWithReason => objects.Count(entry => entry is TObject);

    private static string ItemWhenPerformed<TObject>(IEnumerable<ObjectWithReason> objects, StepOutcome step, string label) where TObject : ObjectWithReason =>
        step == StepOutcome.Performed ? $"{TextFormat.Count(Count<TObject>(objects))} {label}, " : string.Empty;

    private static bool IsFollowUp(ObjectWithReason entry) =>
        entry is ObjectWithReason.Touching or ObjectWithReason.LostSupport or ObjectWithReason.TouchingAnotherModsObject or ObjectWithReason.LostSupportWithAnotherModsObject;

    /// <summary>" (2 of them first shrunk or turned)", or nothing when none was.</summary>
    private static string FirstShrunkOrTurned(IEnumerable<ObjectWithReason> followUps, IRemovalDecisions final)
    {
        var firstFitted = final.Rounds.Where(round => round.Kind == RoundKind.Fit).SelectMany(round => final.FittedAtFirstIn(round)).Select(fitted => fitted.Index).ToHashSet();
        var count = followUps.Count(entry => firstFitted.Contains(entry.TargetIndex));
        return count > 0 ? $" ({TextFormat.Count(count)} of them first shrunk or turned)" : string.Empty;
    }
}
