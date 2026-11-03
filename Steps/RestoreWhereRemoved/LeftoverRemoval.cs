using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>The leftovers in the restored areas judged, and the decisions and other mods' objects once those removed are gone.</summary>
/// <param name="Judged">In target order.</param>
/// <param name="Rounds">The leftovers' round and the rounds that follow it, in order, for the run's decisions to apply; none when no leftover was proposed.</param>
/// <param name="Decisions">The decisions with those rounds.</param>
/// <param name="OtherModsObjects">What became of other mods' objects under those decisions.</param>
internal sealed record LeftoversRemoved(
    ImmutableArray<LeftoverJudged> Judged, ImmutableArray<ProposedRound> Rounds, IRemovalDecisions Decisions, OtherModsObjectsResult OtherModsObjects);

/// <summary>
/// Removes the leftovers of the cleaned mod where an area is restored, with the never-rules of the other removal steps: a kind a
/// left-behind step always keeps is not proposed, and the leftovers' round keeps an object to keep, as every round does. The other mods'
/// objects the removed ones held up are settled by their own step. Everything is decided on a working copy.
/// </summary>
internal static class LeftoverRemoval
{
    /// <summary>A leftover kept shrunk or turned still stands where it was fitted, so the round decides it too.</summary>
    private static readonly RoundOpening Opening = new(RoundKind.RestoredArea, MayReplaceFits: true);

    /// <param name="found">Each leftover once, with its column, in target order.</param>
    /// <param name="final">The decisions the areas were judged by.</param>
    /// <param name="otherModsObjects">What the step for other mods' objects left without support found.</param>
    public static LeftoversRemoved Remove(
        List<(TargetId Target, RestoredColumn Column)> found,
        ImmutableArray<TargetObject> targets,
        IRemovalDecisions final,
        OtherModsObjectsResult otherModsObjects,
        WhereRemovedRestorer.LeftoverRules rules)
    {
        var kinds = found.ToDictionary(entry => entry.Target, entry => rules.VisibleKinds.KindKeeping(targets[entry.Target.Index]) ?? rules.InvisibleKinds.KindKeeping(targets[entry.Target.Index]));
        List<ProposedRemoval> proposals = [.. found.Where(entry => kinds[entry.Target] is null && final.Of(entry.Target) is not Decision.Kept).Select(entry => new ProposedRemoval(entry.Target, new LeftoverInRestoredArea()))];
        if (proposals.Count == 0) return new LeftoversRemoved(JudgeAll(found, kinds, final), [], final, otherModsObjects);

        var copy = final.WorkingCopy();
        var decided = copy.DecideRound(Opening, proposals);
        var judged = JudgeAll(found, kinds, decided);
        if (decided.LeftIn(decided.Rounds[^1]).IsEmpty) return new LeftoversRemoved(judged, [new ProposedRound(Opening, proposals)], decided, otherModsObjects);

        var settled = rules.OtherModsObjects.Settle(otherModsObjects, decided);
        var withFollowUps = decided;
        foreach (var round in settled.Rounds) withFollowUps = copy.DecideRound(round.Opening, round.Proposals);
        return new LeftoversRemoved(judged, [new ProposedRound(Opening, proposals), .. settled.Rounds], withFollowUps, settled.Result);
    }

    /// <param name="decided">The decisions with the leftovers' round, when one was proposed.</param>
    private static ImmutableArray<LeftoverJudged> JudgeAll(
        List<(TargetId Target, RestoredColumn Column)> found, Dictionary<TargetId, AlwaysKeptKind?> kinds, IRemovalDecisions decided) =>
    [
        .. found.Select(entry => new LeftoverJudged(
            entry.Target, entry.Column, kinds[entry.Target] is null && decided.Of(entry.Target) is Decision.Kept kept ? kept.KeepReason : null, kinds[entry.Target])),
    ];
}
