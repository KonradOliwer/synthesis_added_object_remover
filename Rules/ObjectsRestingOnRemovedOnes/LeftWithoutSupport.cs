using System.Collections.Concurrent;
using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// The weight of the surface touching anything of each judged object found so far in the world before any decision; no decision
/// changes it.
/// </summary>
internal sealed record WeightsBefore(ConcurrentDictionary<OtherId, float> WeightOf);

/// <summary>
/// RemoveFloatingObject and PullObjectToTheGround, and KeepSupportingObject only to find who loses its support. An object is left without support when it loses its support
/// (<see cref="SupportLoss"/>) between the world before any decision and the world with the targets removed, the targets shrunk
/// or turned at their new placement, and the objects already found left without support taken away.
/// The rounds start from the changed targets; each round judges the judged objects whose box comes within the gap of a supporter
/// changed in the round before, against the world after that round, so the result does not depend on order. An object the patch
/// cannot change (a later plugin wins it, or it stands in a cell the final world deletes) stays as it is, and so, with
/// RemoveFloatingObject, does one that may not be removed; both keep holding up others.
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
    SupportLoss loss,
    DropFinder drops,
    OtherModsObjectsSettings settings,
    WeightsBefore before,
    Execution execution)
{
    private readonly LosingSupportFates _fates = new(rules, drops, settings);

    /// <summary>What one settling has found so far.</summary>
    /// <param name="Changed">The objects moved (their new placement) or taken away (null): removed, or waiting to be pulled down.</param>
    /// <param name="Decided">Each object whose fate is known.</param>
    /// <param name="ToPull">The objects waiting to be pulled down, with the share of support they lost.</param>
    private sealed record Progress(
        Dictionary<OtherId, PlacedTransform?> Changed,
        Dictionary<OtherId, OtherModsObjectOutcome> Decided,
        List<(OtherObject Object, float LostShare)> ToPull);

    /// <param name="changed">The target objects removed, shrunk or turned, and the objects of <paramref name="othersChanged"/>, where they stood.</param>
    /// <param name="othersChanged">The objects of any plugin moved (their new placement) or taken away (null) before any is judged; none of them is judged.</param>
    /// <returns>Each object left without support once, in id order.</returns>
    public ImmutableArray<OtherModsObjectOutcome> Settle(IReadOnlyList<BoxInSpace> changed, IReadOnlyDictionary<OtherId, PlacedTransform?> othersChanged)
    {
        var progress = new Progress(new Dictionary<OtherId, PlacedTransform?>(othersChanged), [], []);
        IReadOnlyList<BoxInSpace> frontier = changed;
        while (frontier.Count > 0) frontier = DecideRound(frontier, progress);
        PullDownLowestFirst(progress);
        return [.. progress.Decided.Values.OrderBy(outcome => outcome.Object.Id.Index)];
    }

    /// <summary>The judged objects that lose their support once the objects of <paramref name="othersChanged"/> changed, without deciding anything for them.</summary>
    /// <param name="changed">The target objects that left, and the objects of <paramref name="othersChanged"/>, where they stood.</param>
    /// <param name="othersChanged">The objects of any plugin moved (their new placement) or taken away (null); none of them is judged.</param>
    /// <returns>In id order.</returns>
    public ImmutableArray<OtherObject> FindLosingSupport(IReadOnlyList<BoxInSpace> changed, IReadOnlyDictionary<OtherId, PlacedTransform?> othersChanged) =>
        [.. FindLosing(changed, new Progress(new Dictionary<OtherId, PlacedTransform?>(othersChanged), [], [])).Select(entry => entry.Object)];

    /// <returns>The objects this round took away, where they stood.</returns>
    private List<BoxInSpace> DecideRound(IReadOnlyList<BoxInSpace> changedLastRound, Progress progress)
    {
        var takenAway = new List<BoxInSpace>();
        foreach (var (candidate, lost) in FindLosing(changedLastRound, progress))
        {
            if (Decide(candidate, lost, progress)) takenAway.Add(new BoxInSpace(candidate.SpaceKey, rules.BoxOf(candidate, candidate.Transform)));
        }
        return takenAway;
    }

    /// <returns>The judged objects not yet decided or changed near <paramref name="frontier"/> that lose their support in the world of <paramref name="progress"/>, in id order.</returns>
    private List<(OtherObject Object, float LostShare)> FindLosing(IReadOnlyList<BoxInSpace> frontier, Progress progress)
    {
        var candidates = near.FindTouchingAny(frontier, isLeftOut: id => progress.Decided.ContainsKey(id) || progress.Changed.ContainsKey(id));
        FindMissingWeightsBefore(candidates);
        var world = WorldOf(progress);
        var lostShares = ParallelMap.Run(
            execution,
            candidates.Count,
            () => new OtherObjectQueries(),
            (k, queries) => FindLostShare(candidates[k], world, queries),
            ParallelMap.AutomaticRangeSize);
        return [.. candidates.Zip(lostShares).Where(entry => entry.Second is not null).Select(entry => (entry.First, entry.Second!.Value))];
    }

    private void FindMissingWeightsBefore(List<OtherObject> candidates)
    {
        List<OtherObject> missing = [.. candidates.Where(candidate => !before.WeightOf.ContainsKey(candidate.Id))];
        var found = ParallelMap.Run(
            execution,
            missing.Count,
            () => new OtherObjectQueries(),
            (k, queries) => loss.FindWeightBefore(rules.StandingOf(missing[k]), original, queries),
            ParallelMap.AutomaticRangeSize);
        // Two settlings sharing the memo at the same time may both add a weight; both measured it in the same world.
        for (var k = 0; k < missing.Count; k++) before.WeightOf.TryAdd(missing[k].Id, found[k]);
    }

    /// <returns>The share of its support the object lost in <paramref name="world"/>, when it is left without support; null otherwise.</returns>
    private float? FindLostShare(OtherObject candidate, SupportWorld world, OtherObjectQueries queries) =>
        loss.FindLostShare(rules.StandingOf(candidate), before.WeightOf[candidate.Id], original, world, queries);

    /// <returns>Whether the object is taken away: removed, or waiting to be pulled down.</returns>
    private bool Decide(OtherObject other, float lostShare, Progress progress)
    {
        if (_fates.FateBeforePulling(other, lostShare) is not { } outcome) return TakeAwayToPull(other, lostShare, progress);
        return outcome.IsRemoved ? Remove(outcome, progress) : Leave(outcome, progress);
    }

    /// <returns>False: the object stays where it stands and keeps holding up others.</returns>
    private static bool Leave(OtherModsObjectOutcome outcome, Progress progress)
    {
        progress.Changed.Remove(outcome.Object.Id);
        progress.Decided.Add(outcome.Object.Id, outcome);
        return false;
    }

    /// <returns>True: the object is taken away.</returns>
    private static bool Remove(OtherModsObjectOutcome outcome, Progress progress)
    {
        progress.Changed[outcome.Object.Id] = null;
        progress.Decided.Add(outcome.Object.Id, outcome);
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
        var queries = new OtherObjectQueries();
        foreach (var (other, lostShare) in progress.ToPull.OrderBy(entry => BottomOf(entry.Object)).ThenBy(entry => entry.Object.Id.Index))
        {
            PullDown(_fates.FateOfPull(other, lostShare, drops.FindDrop(rules.StandingOf(other), WorldOf(progress), queries)), progress);
        }
    }

    /// <summary>The world after with the objects as <paramref name="progress"/> has changed them so far.</summary>
    private SupportWorld WorldOf(Progress progress) => final.With(progress.Changed);

    private float BottomOf(OtherObject other) => rules.BoxOf(other, other.Transform).WorldAabb(0f).Min.Z;

    private static void PullDown(OtherModsObjectOutcome outcome, Progress progress)
    {
        if (outcome.IsRemoved)
        {
            Remove(outcome, progress);
        }
        else if (outcome.Fate != OtherModsObjectFate.PulledDown)
        {
            Leave(outcome, progress);
        }
        else
        {
            progress.Changed[outcome.Object.Id] = outcome.Object.Transform with { Position = outcome.NewPosition };
            progress.Decided.Add(outcome.Object.Id, outcome);
        }
    }
}
