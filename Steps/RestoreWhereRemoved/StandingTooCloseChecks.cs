using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What the too-close check of the cleaned mod's objects that stand found in one pass.</summary>
/// <param name="Refused">The candidates going back that make a standing object too close, each with the first such object.</param>
/// <param name="CopiesLost">The candidates going back with a copy whose copy is too close, by candidate index.</param>
internal sealed record StandingTooCloseFound(IReadOnlyDictionary<int, PlacedRefusal> Refused, IReadOnlyDictionary<int, CloneLoss> CopiesLost);

/// <summary>
/// The too-close step's check, as the next patcher makes it, of the cleaned mod's objects that stand in the planned world: each
/// target that may stay and each refused candidate that is a target, where it stands, with its own group's settings, and each copy,
/// with the settings of the cleaned mod's own objects. They are measured among the other mods' objects the removal steps judge by,
/// where the run leaves them, less those that end removed in the pass. No candidate is a cause: the next patcher reads a refused one
/// as a target and one back at its earlier place as support only, and a copy causes no removal for its own cleaned mod.
/// <list type="bullet">
/// <item>A copy too close to a cause is lost.</item>
/// <item>A standing object counts only when its check with nothing going back finds no cause: then every cause it is too close to
/// stands only because candidates go back, and those candidates are refused. When its check stops, the candidates that make a cause
/// within its zones stand are refused, with the failure.</item>
/// </list>
/// Thread-safe.
/// </summary>
/// <param name="world">The standing objects whose zones reach a cause that ends removed with nothing going back, then every planned
/// copy, as its targets with their position as id; every cause their zones reach as its other-mod objects.</param>
/// <param name="settingsOf">The settings of each of the world's targets, by its position.</param>
/// <param name="candidateOfStanding">The candidate index of each of the world's standing objects that is a candidate; null for a target that may stay.</param>
/// <param name="copyPositions">The position among the world's targets of each planned copy, by its candidate index.</param>
/// <param name="leftOutWithNothingGoingBack">The world's causes that end removed with nothing going back.</param>
internal sealed class StandingTooCloseChecks(
    CollectedObjects world,
    ImmutableArray<TooCloseSettings> settingsOf,
    ImmutableArray<int?> candidateOfStanding,
    IReadOnlyDictionary<int, int> copyPositions,
    IReadOnlySet<OtherId> leftOutWithNothingGoingBack,
    TooCloseSettings cleanedModsObjects,
    FinalPresence presence,
    ITooCloseToWhatStaysRule rule,
    Execution execution)
{
    /// <summary>Whether a standing object's check with nothing going back finds a cause, by its position among the world's targets.</summary>
    private readonly ComputedOncePerKey<int, bool> _foundWithNothingGoingBack = new(Publication.FirstWriteWins, EqualityComparer<int>.Default);

    /// <param name="candidates">The placed records whose areas let them go back, in candidate order.</param>
    /// <param name="targetsThatMayStay">The targets that are not removed and are no candidates, where they stand last.</param>
    /// <param name="withCopy">The candidates planned to go back with a copy; a pass only ever loses copies.</param>
    public static StandingTooCloseChecks Of(
        CollectedObjects collected,
        OtherModsObjectsResult otherModsObjects,
        AfterRemovalWorld world,
        IReadOnlyList<RecordChangedByCleanedMod> candidates,
        IReadOnlyList<TargetObject> targetsThatMayStay,
        IReadOnlySet<int> withCopy,
        WhereRemovedRestorer.RemovalStepJudging judging,
        FinalPresence presence,
        ITooCloseToWhatStaysRule rule,
        Execution execution)
    {
        var standing = StandingObjects(world, candidates, targetsThatMayStay, judging);
        var others = CausesWhereTheRunLeavesThem(collected, otherModsObjects, candidates);
        List<OtherObject> removedWithNothingGoingBack = [.. others.Where(other => presence.EndsRemoved(other.Key, _ => false))];
        var checkedStanding = rule.FindObjectsReaching([.. standing.Select(entry => (entry.Object, entry.Settings))], removedWithNothingGoingBack);
        var copies = world.ClonesOf(withCopy).ToList();
        if (copies.Count != withCopy.Count) throw new InvalidOperationException($"{withCopy.Count} placed records are planned with a copy, but {copies.Count} of them stand now.");
        List<(TargetObject Object, TooCloseSettings Settings)> objects =
            [.. checkedStanding.Select(position => (standing[position].Object, standing[position].Settings)), .. copies.Select(copy => (copy, judging.CleanedModsObjects.TooClose))];
        ImmutableArray<OtherObject> causes = [.. rule.FindOthersReached(objects, others).Select((position, index) => others[position] with { Id = new OtherId(index) })];
        var checkedWorld = world.World with
        {
            Targets = [.. objects.Select((entry, position) => entry.Object with { Id = new TargetId(position) })],
            OtherModObjects = causes,
            SupportOnlyObjects = [],
            Order = new WorkOrder([.. Enumerable.Range(0, objects.Count)]),
        };
        return new StandingTooCloseChecks(
            checkedWorld,
            [.. objects.Select(entry => entry.Settings)],
            [.. checkedStanding.Select(position => standing[position].Candidate)],
            withCopy.Order().Select((candidate, index) => KeyValuePair.Create(candidate, checkedStanding.Length + index)).ToDictionary(),
            causes.Where(cause => presence.EndsRemoved(cause.Key, _ => false)).Select(cause => cause.Id).ToHashSet(),
            judging.CleanedModsObjects.TooClose,
            presence,
            rule,
            execution);
    }

    /// <summary>The targets that may stay, then the candidates that stand now and are targets, each where it stands, with its group's settings.</summary>
    private static List<(TargetObject Object, TooCloseSettings Settings, int? Candidate)> StandingObjects(
        AfterRemovalWorld world, IReadOnlyList<RecordChangedByCleanedMod> candidates, IReadOnlyList<TargetObject> targetsThatMayStay, WhereRemovedRestorer.RemovalStepJudging judging)
    {
        var settingsOf = judging.TooClose.SelectMany(group => group.Objects.Select(target => KeyValuePair.Create(target, group.Settings))).ToDictionary();
        return
        [
            .. targetsThatMayStay.Where(target => settingsOf.ContainsKey(target.Id)).Select(target => (target, settingsOf[target.Id], (int?)null)),
            .. world.Now
                .Select(now => (Now: now, Candidate: world.StandingNow[now.Id.Index]))
                .Where(entry => candidates[entry.Candidate].Target is { } target && settingsOf.ContainsKey(target))
                .Select(entry => (entry.Now, settingsOf[candidates[entry.Candidate].Target!.Value], (int?)entry.Candidate)),
        ];
    }

    /// <summary>The other mods' objects the removal steps judge by, pulled down where they land and removed left out, but the candidates.</summary>
    private static List<OtherObject> CausesWhereTheRunLeavesThem(CollectedObjects collected, OtherModsObjectsResult otherModsObjects, IReadOnlyList<RecordChangedByCleanedMod> candidates)
    {
        var takenAway = otherModsObjects.TakenAway();
        var candidateKeys = candidates.Select(change => change.Key).ToHashSet();
        return
        [
            .. collected.OtherModObjects
                .Select(other => takenAway.TryGetValue(other.Id, out var after) ? after : other)
                .OfType<OtherObject>()
                .Where(other => !candidateKeys.Contains(other.Key)),
        ];
    }

    /// <param name="goingBack">The indexes of the candidates going back.</param>
    /// <param name="withClone">Those of them going back with a copy.</param>
    public StandingTooCloseFound Check(IReadOnlySet<int> goingBack, IReadOnlySet<int> withClone, PresenceInPass pass)
    {
        HashSet<OtherId> leftOut = [.. world.OtherModObjects.Where(cause => pass.EndsRemoved(cause.Key)).Select(cause => cause.Id)];
        return new StandingTooCloseFound(RefuseForStanding(goingBack, leftOut, pass), FindCopiesLost(withClone, leftOut));
    }

    private Dictionary<int, CloneLoss> FindCopiesLost(IReadOnlySet<int> withClone, HashSet<OtherId> leftOut)
    {
        if (withClone.Count == 0) return [];
        var tooClose = rule.Create(world, leftOut.OrderBy(cause => cause.Index), cleanedModsObjects);
        int[] candidates = [.. withClone.Order()];
        var checks = ParallelMap.Run(execution, candidates.Length, position => tooClose.CheckWhereItStands(CopyOf(candidates[position])), ParallelMap.AutomaticRangeSize);
        return candidates.Zip(checks)
            .Where(entry => entry.Second.Clash is not null || entry.Second.Failure is not null)
            .ToDictionary(entry => entry.First, entry => new CloneLoss(CloneRefusal.TooCloseAsCleanedModsObject, entry.Second.Failure));
    }

    /// <summary>The refusals of the standing objects whose zones reach a cause that stands in the pass but not with nothing going back.</summary>
    private Dictionary<int, PlacedRefusal> RefuseForStanding(IReadOnlySet<int> goingBack, HashSet<OtherId> leftOut, PresenceInPass pass)
    {
        List<OtherObject> standingOnlyNow = [.. world.OtherModObjects.Where(cause => !leftOut.Contains(cause.Id) && leftOutWithNothingGoingBack.Contains(cause.Id))];
        if (standingOnlyNow.Count == 0) return [];
        List<int> reaching = [.. rule.FindObjectsReaching([.. candidateOfStanding.Select((_, position) => (world.Targets[position], settingsOf[position]))], standingOnlyNow)
            .Where(position => StandsInPass(position, goingBack, pass))];
        var clashes = ParallelMap.Run(
            execution, reaching.Count, index => rule.FindEveryClash(world, leftOut.OrderBy(cause => cause.Index), settingsOf[reaching[index]], world.Targets[reaching[index]]), ParallelMap.AutomaticRangeSize);
        var first = new Dictionary<int, PlacedRefusal>();
        foreach (var (position, found) in reaching.Zip(clashes))
        {
            foreach (var (candidate, refusal) in RefusalsOf(position, found, standingOnlyNow, goingBack)) first.TryAdd(candidate, refusal);
        }
        return first;
    }

    /// <summary>
    /// The candidates the standing object's check refuses, ascending: none when it finds nothing, or when its check with nothing going
    /// back finds a cause already.
    /// </summary>
    private IEnumerable<(int Candidate, PlacedRefusal Refusal)> RefusalsOf(int position, TooCloseClashes found, List<OtherObject> standingOnlyNow, IReadOnlySet<int> goingBack)
    {
        if ((found.Others.IsEmpty && found.Failure is null) || FoundWithNothingGoingBack(position)) return [];
        var refusal = found.Failure is { } failure ? TooCloseRefusals.Stopped(failure) : TooCloseRefusals.ByWhatStays(world.Targets[position].Key);
        var causes = found.Failure is null
            ? found.Others.Select(id => world.OtherModObjects[id.Index])
            : rule.FindOthersReached([(world.Targets[position], settingsOf[position])], standingOnlyNow).Select(index => standingOnlyNow[index]);
        return causes.SelectMany(cause => CandidatesMakingItStand(cause, goingBack)).Distinct().Order().Select(candidate => (candidate, refusal));
    }

    /// <summary>A target that may stay stands when it does not end removed; a refused candidate when it does not go back either.</summary>
    private bool StandsInPass(int position, IReadOnlySet<int> goingBack, PresenceInPass pass) =>
        (candidateOfStanding[position] is not { } candidate || !goingBack.Contains(candidate)) && !pass.EndsRemoved(world.Targets[position].Key);

    /// <remarks>A check that stops finds nothing here, so the check in the pass refuses or reports the object.</remarks>
    private bool FoundWithNothingGoingBack(int position) =>
        _foundWithNothingGoingBack.Get(
            position,
            () => rule.Create(world, leftOutWithNothingGoingBack.OrderBy(cause => cause.Index), settingsOf[position]).CheckWhereItStands(world.Targets[position]).Clash is not null);

    /// <summary>
    /// The candidates going back without which the cause would end removed, ascending. When several make it stand each on its own,
    /// leaving out none alone removes it: then those on its enable-parent chain whose restore brings back their presence.
    /// </summary>
    private IEnumerable<int> CandidatesMakingItStand(OtherObject cause, IReadOnlySet<int> goingBack)
    {
        List<int> alone = [.. goingBack.Where(candidate => presence.EndsRemoved(cause.Key, other => other != candidate && goingBack.Contains(other)))];
        if (alone.Count > 0) return alone;
        List<int> onChain = [.. presence.CandidatesRestoringPresenceOnChainOf(cause.Key, goingBack.Contains)];
        return onChain.Count > 0 ? onChain : throw new UnreachableException($"{cause.Key} stands only once candidates go back, but none on its enable-parent chain brings back its presence.");
    }

    private TargetObject CopyOf(int candidate) =>
        copyPositions.TryGetValue(candidate, out var position)
            ? world.Targets[position]
            : throw new InvalidOperationException($"The copy of candidate {candidate} is checked although no copy of it was planned.");
}
