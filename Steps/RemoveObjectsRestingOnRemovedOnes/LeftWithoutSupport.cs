using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>What a judged object had before any decision, which no decision changes.</summary>
/// <param name="Weight">The weight of its surface touching anything.</param>
/// <param name="Rested">Whether it rested on what was below it.</param>
internal readonly record struct SupportBefore(float Weight, bool Rested);

/// <summary>The <see cref="SupportBefore"/> of each judged object found so far, kept by one step for all its settlings.</summary>
internal sealed record SupportsBefore(Dictionary<OtherId, SupportBefore> Of);

/// <summary>
/// RemoveFloatingObject and PullObjectToTheGround. An object is left without support when it lost at least the threshold share of
/// the weight of its surface touching anything: lost = 1 - (weight touching now) / (weight touching before), with the targets
/// removed, the targets shrunk or turned at their new placement, and the objects already found left without support taken away;
/// and when it rested on what was below it before and can now move down by more than the gap.
/// The rounds start from the changed targets; each round judges the judged objects whose box comes within the gap of a supporter
/// changed in the round before, against the world after that round, so the result does not depend on order. An object the patch
/// cannot change stays as it is, and so, with RemoveFloatingObject, does one that may not be removed; both keep holding up others.
/// Pulling then moves the objects one by one, lowest first, each onto what is below it with the objects pulled before at their new
/// place, so a stack lands from the bottom up.
/// </summary>
/// <param name="original">The world before any decision.</param>
/// <param name="final">The world with the target objects as decided and the objects of any plugin unchanged.</param>
/// <param name="before">Filled here with the objects not yet in it; it holds only what does not depend on any decision.</param>
internal sealed class LeftWithoutSupport(
    SupportWorld original,
    SupportWorld final,
    JudgedObjectsNear near,
    OtherModsObjectRules rules,
    OtherObjectSupport support,
    DropFinder drops,
    OtherModsObjectsSettings settings,
    SupportsBefore before,
    Execution execution)
{
    /// <summary>What one settling has found so far.</summary>
    /// <param name="Changed">The objects moved (their new placement) or taken away (null): removed, or waiting to be pulled down.</param>
    /// <param name="Decided">Each object whose fate is known.</param>
    /// <param name="ToPull">The objects waiting to be pulled down, with the share of support they lost.</param>
    private sealed record Progress(
        Dictionary<OtherId, PlacedTransform?> Changed,
        Dictionary<OtherId, OtherModsObjectOutcome> Decided,
        List<(OtherObject Object, float LostShare)> ToPull);

    /// <param name="changedTargets">The target objects removed, shrunk or turned, where they stood.</param>
    /// <returns>Each object left without support once, in id order.</returns>
    public ImmutableArray<OtherModsObjectOutcome> Settle(IReadOnlyList<BoxInSpace> changedTargets)
    {
        var progress = new Progress([], [], []);
        IReadOnlyList<BoxInSpace> frontier = changedTargets;
        while (frontier.Count > 0) frontier = DecideRound(frontier, progress);
        PullDownLowestFirst(progress);
        return [.. progress.Decided.Values.OrderBy(outcome => outcome.Object.Id.Index)];
    }

    /// <returns>The objects this round took away, where they stood.</returns>
    private List<BoxInSpace> DecideRound(IReadOnlyList<BoxInSpace> changedLastRound, Progress progress)
    {
        var candidates = near.FindTouchingAny(changedLastRound, isLeftOut: id => progress.Decided.ContainsKey(id) || progress.Changed.ContainsKey(id));
        FindMissingSupportsBefore(candidates);
        var world = final.With(new Dictionary<OtherId, PlacedTransform?>(progress.Changed));
        var lostShares = ParallelMap.Run(
            execution,
            candidates.Count,
            () => new OtherObjectQueries(),
            (k, queries) => FindSupportLost(candidates[k], before.Of[candidates[k].Id], world, queries),
            ParallelMap.AutomaticRangeSize);
        var takenAway = new List<BoxInSpace>();
        for (var k = 0; k < candidates.Count; k++)
        {
            if (lostShares[k] is { } lost && Decide(candidates[k], lost, progress))
            {
                takenAway.Add(new BoxInSpace(candidates[k].SpaceKey, rules.BoxOf(candidates[k], candidates[k].Transform)));
            }
        }
        return takenAway;
    }

    private void FindMissingSupportsBefore(List<OtherObject> candidates)
    {
        List<OtherObject> missing = [.. candidates.Where(candidate => !before.Of.ContainsKey(candidate.Id))];
        var found = ParallelMap.Run(
            execution,
            missing.Count,
            () => new OtherObjectQueries(),
            (k, queries) => new SupportBefore(support.FindContacts(missing[k], original, queries).TotalWeight, drops.Rests(missing[k], original, queries)),
            ParallelMap.AutomaticRangeSize);
        for (var k = 0; k < missing.Count; k++) before.Of.Add(missing[k].Id, found[k]);
    }

    /// <returns>
    /// The share of its support the object lost, when that reaches the threshold, the object rested on something before and can
    /// move down by more than the gap now; null otherwise. An object that touched nothing, or only touched things beside it while
    /// nothing was right below it, was not resting. An object that only lost what touched it from above or from the side still
    /// rests on what is below it.
    /// </returns>
    private float? FindSupportLost(OtherObject candidate, SupportBefore supportBefore, SupportWorld world, OtherObjectQueries queries)
    {
        if (!(supportBefore.Weight > 0f) || !supportBefore.Rested) return null;
        var lost = 1f - support.FindContacts(candidate, world, queries).TotalWeight / supportBefore.Weight;
        return SupportRule.ReachesThreshold(lost, settings.SupportLostFraction) && !drops.Rests(candidate, world, queries) ? lost : null;
    }

    /// <returns>Whether the object is taken away: removed, or waiting to be pulled down.</returns>
    private bool Decide(OtherObject other, float lostShare, Progress progress)
    {
        if (!rules.PatchCanChange(other)) return Leave(other, OtherModsObjectFate.LeftWonAfterPatch, lostShare, ReferenceRoles.None, progress);
        if (settings.Handling == OtherModsObjectsHandling.PullObjectToTheGround) return TakeAwayToPull(other, lostShare, progress);
        return OtherModsObjectRules.RemovalBlockers(other) is var blockers and not ReferenceRoles.None
            ? Leave(other, OtherModsObjectFate.LeftNotRemovable, lostShare, blockers, progress)
            : Remove(other, OtherModsObjectFate.Removed, lostShare, progress);
    }

    /// <returns>False: the object stays where it stands and keeps holding up others.</returns>
    private static bool Leave(OtherObject other, OtherModsObjectFate fate, float lostShare, ReferenceRoles blockers, Progress progress)
    {
        progress.Changed.Remove(other.Id);
        progress.Decided.Add(other.Id, new OtherModsObjectOutcome(other, fate, lostShare, Drop: 0f, blockers));
        return false;
    }

    /// <returns>True: the object is taken away.</returns>
    private static bool Remove(OtherObject other, OtherModsObjectFate fate, float lostShare, Progress progress)
    {
        progress.Changed[other.Id] = null;
        progress.Decided.Add(other.Id, new OtherModsObjectOutcome(other, fate, lostShare, Drop: 0f, ReferenceRoles.None));
        return true;
    }

    /// <summary>Until it is pulled down, the object is out of the world, so nothing rests on it where it stood.</summary>
    /// <returns>True: the object is taken away.</returns>
    private static bool TakeAwayToPull(OtherObject other, float lostShare, Progress progress)
    {
        progress.Changed[other.Id] = null;
        progress.ToPull.Add((other, lostShare));
        return true;
    }

    /// <summary>One at a time, lowest bottom first, then by id; each against the world the ones before it left.</summary>
    private void PullDownLowestFirst(Progress progress)
    {
        var world = final.With(progress.Changed);
        var queries = new OtherObjectQueries();
        foreach (var (other, lostShare) in progress.ToPull.OrderBy(entry => BottomOf(entry.Object)).ThenBy(entry => entry.Object.Id.Index))
        {
            PullDown(other, lostShare, drops.FindDrop(other, world, queries), progress);
        }
    }

    private float BottomOf(OtherObject other) => rules.BoxOf(other, other.Transform).WorldAabb(0f).Min.Z;

    /// <param name="drop">Null when nothing is below it within the pull limit.</param>
    private void PullDown(OtherObject other, float lostShare, float? drop, Progress progress)
    {
        if (drop is not { } distance)
        {
            if (OtherModsObjectRules.RemovalBlockers(other) is var blockers and not ReferenceRoles.None)
            {
                Leave(other, OtherModsObjectFate.LeftNothingBelowNotRemovable, lostShare, blockers, progress);
            }
            else
            {
                Remove(other, OtherModsObjectFate.RemovedNothingBelow, lostShare, progress);
            }
        }
        else if (drops.RestsAt(distance))
        {
            Leave(other, OtherModsObjectFate.LeftStillTouching, lostShare, ReferenceRoles.None, progress);
        }
        else
        {
            var moved = new OtherModsObjectOutcome(other, OtherModsObjectFate.PulledDown, lostShare, distance, ReferenceRoles.None);
            progress.Changed[other.Id] = other.Transform with { Position = moved.NewPosition };
            progress.Decided.Add(other.Id, moved);
        }
    }
}
