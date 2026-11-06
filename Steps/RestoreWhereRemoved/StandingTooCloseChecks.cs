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
internal sealed record StandingTooCloseFound(IReadOnlyDictionary<int, PlacedRefusal> Refused, IReadOnlyDictionary<int, CloneLoss> CopiesLost)
{
    public static StandingTooCloseFound None() => new(new Dictionary<int, PlacedRefusal>(), new Dictionary<int, CloneLoss>());

    /// <summary>
    /// The objects the ground going down started to pull down too close to a standing object or copy, where they stand now, in id
    /// order: the ground under them stays.
    /// </summary>
    public IReadOnlyList<OtherObject> GroundKeptUnder { get; init; } = [];
}

/// <summary>
/// The too-close rule's check, as the next patcher makes it, of the cleaned mod's objects that stand in the planned world: each
/// target that may stay and each refused candidate that is a target, where it stands, with its own group's settings, and each copy,
/// with the settings of the cleaned mod's own objects. They are measured among the other mods' objects the removal steps judge by,
/// where the run leaves them, less those that end removed in the pass. No candidate is a cause: the next patcher reads a refused one
/// as a target and one back at its earlier place as support only, and a copy causes no removal for its own cleaned mod.
/// <list type="bullet">
/// <item>A copy too close to a cause is lost.</item>
/// <item>A standing object counts only when its check with nothing going back finds no cause: then every cause it is too close to
/// stands only because candidates go back, and those candidates are refused. When its check stops, the candidates that make a cause
/// within its zones stand are refused, with the failure.</item>
/// <item>Every object the follow-up of the pass pulls down is a cause where it lands, not where it stood, whatever list it came from:
/// the patch gives it a position no trusted version holds, so it is no longer read as support only. A standing object that counts or a
/// copy too close to one where it lands refuses the candidates that pulled it down, a candidate pulled where it arrives itself, so it
/// stays where it stood; when the ground going down pulled it, the ground under it stays.</item>
/// </list>
/// Thread-safe.
/// </summary>
/// <param name="world">The standing objects, then every planned copy, as its targets with their position as id; every cause their
/// zones reach as its other-mod objects.</param>
/// <param name="settingsOf">The settings of each of the world's targets, by its position.</param>
/// <param name="candidateOfStanding">The candidate index of each of the world's standing objects that is a candidate; null for a target that may stay.</param>
/// <param name="copyPositions">The position among the world's targets of each planned copy, by its candidate index.</param>
/// <param name="copyCandidates">The candidate index of each planned copy, in the order of their positions after the standing objects.</param>
/// <param name="leftOutWithNothingGoingBack">The world's causes that end removed with nothing going back.</param>
/// <param name="candidateOf">The index of each candidate, by its key.</param>
internal sealed class StandingTooCloseChecks(
    CollectedObjects world,
    ImmutableArray<TooCloseSettings> settingsOf,
    ImmutableArray<int?> candidateOfStanding,
    IReadOnlyDictionary<int, int> copyPositions,
    ImmutableArray<int> copyCandidates,
    IReadOnlySet<OtherId> leftOutWithNothingGoingBack,
    IReadOnlyDictionary<RecordKey, int> candidateOf,
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
        IReadOnlyDictionary<TargetId, TooCloseSettings> settingsOfGroupedTargets,
        TooCloseSettings cleanedModsObjectsSettings,
        FinalPresence presence,
        ITooCloseToWhatStaysRule rule,
        Execution execution)
    {
        var standing = StandingObjects(world, candidates, targetsThatMayStay, settingsOfGroupedTargets);
        var others = CausesWhereTheRunLeavesThem(collected, otherModsObjects, candidates);
        var copies = world.ClonesOf(withCopy).ToList();
        if (copies.Count != withCopy.Count) throw new InvalidOperationException($"{withCopy.Count} placed records are planned with a copy, but {copies.Count} of them stand now.");
        List<(TargetObject Object, TooCloseSettings Settings)> objects =
            [.. standing.Select(entry => (entry.Object, entry.Settings)), .. copies.Select(copy => (copy, cleanedModsObjectsSettings))];
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
            [.. standing.Select(entry => entry.Candidate)],
            withCopy.Order().Select((candidate, index) => KeyValuePair.Create(candidate, standing.Count + index)).ToDictionary(),
            [.. withCopy.Order()],
            causes.Where(cause => presence.EndsRemoved(cause.Key, _ => false)).Select(cause => cause.Id).ToHashSet(),
            candidates.Select((change, index) => KeyValuePair.Create(change.Key, index)).ToDictionary(),
            cleanedModsObjectsSettings,
            presence,
            rule,
            execution);
    }

    /// <summary>The targets that may stay, then the candidates that stand now and are targets, each where it stands, with its group's settings.</summary>
    private static List<(TargetObject Object, TooCloseSettings Settings, int? Candidate)> StandingObjects(
        AfterRemovalWorld world, IReadOnlyList<RecordChangedByCleanedMod> candidates, IReadOnlyList<TargetObject> targetsThatMayStay, IReadOnlyDictionary<TargetId, TooCloseSettings> settingsOfGroupedTargets)
    {
        List<(TargetObject Object, TooCloseSettings Settings, int? Candidate)> standing = [];
        foreach (var target in targetsThatMayStay)
        {
            if (settingsOfGroupedTargets.TryGetValue(target.Id, out var settings)) standing.Add((target, settings, null));
        }
        foreach (var now in world.Now)
        {
            var candidate = world.StandingNow[now.Id.Index];
            if (candidates[candidate].Target is { } target && settingsOfGroupedTargets.TryGetValue(target, out var settings)) standing.Add((now, settings, candidate));
        }
        return standing;
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
    /// <param name="followUp">
    /// The follow-up of the pass: its target objects stand nowhere, the objects it removes cause nothing, and those it pulls down cause
    /// only where they land.
    /// </param>
    public StandingTooCloseFound Check(IReadOnlySet<int> goingBack, IReadOnlySet<int> withClone, PresenceInPass pass, FollowUpChecks followUp)
    {
        var removedByFollowUp = followUp.OthersRemoved();
        List<PulledByFollowUp> landed = [.. followUp.PulledDown()];
        var pulledKeys = landed.Select(pulled => pulled.Landed.Key).ToHashSet();
        HashSet<OtherId> leftOut =
            [.. world.OtherModObjects.Where(cause => pass.EndsRemoved(cause.Key) || removedByFollowUp.Contains(cause.Key) || pulledKeys.Contains(cause.Key)).Select(cause => cause.Id)];
        var refused = RefuseForStanding(goingBack, leftOut, pass, followUp.TargetsDecided);
        var beside = FindBesideLanded(landed, leftOut, new InPass(goingBack, withClone, pass, followUp.TargetsDecided));
        foreach (var (candidate, refusal) in beside.SelectMany(clash => RefusalsOf(clash))) refused.TryAdd(candidate, refusal);
        return new StandingTooCloseFound(refused, FindCopiesLost(withClone, leftOut))
        {
            GroundKeptUnder = [.. beside.Select(clash => clash.Pulled.ByGround).OfType<OtherObject>().DistinctBy(other => other.Id).OrderBy(other => other.Id.Index)],
        };
    }

    /// <summary>A standing object or copy too close to an object the follow-up pulls down, with the refusal it gives.</summary>
    private sealed record BesideLanded(PulledByFollowUp Pulled, PlacedRefusal Refusal);

    /// <summary>The candidates that started a pull a standing object is too close to, ascending.</summary>
    private IEnumerable<(int Candidate, PlacedRefusal Refusal)> RefusalsOf(BesideLanded clash) =>
        clash.Pulled.StartedBy.Select(key => candidateOf[key]).Order().Select(candidate => (candidate, clash.Refusal));

    /// <summary>Which candidates go back in a pass, and what its follow-up decides.</summary>
    /// <param name="DecidedByFollowUp">The target objects the follow-up removes or restores.</param>
    private sealed record InPass(IReadOnlySet<int> GoingBack, IReadOnlySet<int> WithClone, PresenceInPass Pass, IReadOnlySet<RecordKey> DecidedByFollowUp);

    /// <summary>
    /// Each cause the follow-up pulls down where a standing object that counts or a copy is too close to it, with that object's
    /// refusal, in position order of those objects.
    /// </summary>
    /// <param name="landed">The causes the follow-up pulls down, where they land, in id order.</param>
    private List<BesideLanded> FindBesideLanded(List<PulledByFollowUp> landed, HashSet<OtherId> leftOut, InPass inPass)
    {
        if (landed.Count == 0) return [];
        var firstLanded = world.OtherModObjects.Length;
        var withLanded = LandedCauses.AddedAfter(world, [.. landed.Select(pulled => pulled.Landed)]);
        List<int> counting = [.. Enumerable.Range(0, world.Targets.Length).Where(position => CountsBesideLanded(position, inPass))];
        List<int> reaching = [.. rule.FindObjectsReaching([.. counting.Select(position => (world.Targets[position], settingsOf[position]))], [.. landed.Select(pulled => pulled.Landed)])
            .Select(index => counting[index])];
        var clashes = FindEveryClash(withLanded, reaching, leftOut);
        return [.. reaching.Zip(clashes).SelectMany(entry => ClashesBesideLanded(entry.First, entry.Second, landed, firstLanded))];
    }

    /// <summary>A copy going back with its candidate counts; a standing object when it stands in the pass and its check with nothing going back finds no cause.</summary>
    private bool CountsBesideLanded(int position, InPass inPass)
    {
        if (position >= candidateOfStanding.Length) return inPass.WithClone.Contains(copyCandidates[position - candidateOfStanding.Length]);
        return StandsInPass(position, inPass.GoingBack, inPass.Pass)
            && !inPass.DecidedByFollowUp.Contains(world.Targets[position].Key)
            && !FoundWithNothingGoingBack(position);
    }

    /// <summary>Every clash of each object at <paramref name="positions"/>, in that order.</summary>
    /// <param name="checkedWorld">The world, or the world with more causes after its own.</param>
    private TooCloseClashes[] FindEveryClash(CollectedObjects checkedWorld, List<int> positions, HashSet<OtherId> leftOut) =>
        ParallelMap.Run(
            execution, positions.Count, index => rule.FindEveryClash(checkedWorld, leftOut.OrderBy(cause => cause.Index), settingsOf[positions[index]], world.Targets[positions[index]]), ParallelMap.AutomaticRangeSize);

    /// <summary>The refusal a standing object or copy at <paramref name="position"/> gives: by that object, or with the failure of its check.</summary>
    private PlacedRefusal RefusalOf(int position, TooCloseClashes found) =>
        found.Failure is { } failure ? TooCloseRefusals.Stopped(failure) : TooCloseRefusals.ByWhatStays(world.Targets[position].Key);

    /// <summary>The landed causes the object is too close to, in id order; every landed cause its zones reach when its check stops.</summary>
    /// <param name="firstLanded">The id of the first landed cause in the checked world.</param>
    private IEnumerable<BesideLanded> ClashesBesideLanded(int position, TooCloseClashes found, List<PulledByFollowUp> landed, int firstLanded)
    {
        var refusal = RefusalOf(position, found);
        var hit = found.Failure is null
            ? found.Others.Where(id => id.Index >= firstLanded).Select(id => landed[id.Index - firstLanded])
            : rule.FindOthersReached([(world.Targets[position], settingsOf[position])], [.. landed.Select(pulled => pulled.Landed)]).Select(index => landed[index]);
        return hit.Select(pulled => new BesideLanded(pulled, refusal));
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
    /// <param name="decidedByFollowUp">The target objects the follow-up removes or restores.</param>
    private Dictionary<int, PlacedRefusal> RefuseForStanding(IReadOnlySet<int> goingBack, HashSet<OtherId> leftOut, PresenceInPass pass, IReadOnlySet<RecordKey> decidedByFollowUp)
    {
        List<OtherObject> standingOnlyNow = [.. world.OtherModObjects.Where(cause => !leftOut.Contains(cause.Id) && leftOutWithNothingGoingBack.Contains(cause.Id))];
        if (standingOnlyNow.Count == 0) return [];
        List<int> reaching = [.. rule.FindObjectsReaching([.. candidateOfStanding.Select((_, position) => (world.Targets[position], settingsOf[position]))], standingOnlyNow)
            .Where(position => StandsInPass(position, goingBack, pass) && !decidedByFollowUp.Contains(world.Targets[position].Key))];
        var clashes = FindEveryClash(world, reaching, leftOut);
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
        var refusal = RefusalOf(position, found);
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
