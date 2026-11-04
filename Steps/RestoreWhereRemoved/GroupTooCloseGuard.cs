using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The too-close step's check of each target that stays, with the settings of its own collision group, against the candidates going
/// back that can cause removals once they are back: one that ends with a part only a plugin the removal steps do not fully believe
/// holds, such as an untrusted later plugin's position or an untrusted earlier version. A later run judges the target against such a
/// candidate with those settings. Only the targets whose zones can reach such a candidate are checked. Thread-safe.
/// </summary>
/// <param name="world">
/// The targets that are checked, where they stand last, with their position as id, and the candidates that stay causes, each as it
/// goes back, as its other-mod objects in candidate order.
/// </param>
/// <param name="settingsOf">The too-close settings of each checked target, by its position.</param>
/// <param name="candidateOf">The candidate index of each of the world's other-mod objects, by its id.</param>
/// <param name="reachOf">The box around each checked target that holds every zone of its group, by its position.</param>
/// <param name="createRule">The too-close rule over a world of what stays with the given causes left out and the given settings.</param>
internal sealed class GroupTooCloseGuard(
    CollectedObjects world,
    ImmutableArray<TooCloseSettings> settingsOf,
    ImmutableArray<int> candidateOf,
    ImmutableArray<Box> reachOf,
    GuardReach reach,
    Func<CollectedObjects, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> createRule,
    Execution execution)
{
    /// <param name="targetsThatMayStay">The targets that are not removed and are no candidates, where they stand last.</param>
    /// <param name="candidates">The placed records whose areas let them go back, in candidate order.</param>
    public static GroupTooCloseGuard Of(
        AfterRemovalWorld world,
        IReadOnlyList<RecordChangedByCleanedMod> candidates,
        IReadOnlyList<TargetObject> targetsThatMayStay,
        WhereRemovedRestorer.RemovalStepJudging judging,
        GuardReach reach,
        PluginName cleanedMod,
        Func<CollectedObjects, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> createRule,
        Execution execution)
    {
        var settingsByTarget = judging.TooClose.SelectMany(group => group.Objects.Select(target => KeyValuePair.Create(target, group.Settings))).ToDictionary();
        List<int> causes = settingsByTarget.Count == 0 ? [] : [.. Enumerable.Range(0, candidates.Count).Where(candidate => StaysACause(candidates[candidate], judging.Accepted))];
        List<OtherObject> copies = [.. causes.Select((candidate, index) => world.Restored[candidate].AsOtherObject(new OtherId(index), cleanedMod))];
        var bounds = copies.Select(copy => (copy.SpaceKey, Box: reach.BoundsOf(copy))).ToList();
        List<(TargetObject Target, TooCloseSettings Settings, Box Reach)> checkedTargets =
        [
            .. targetsThatMayStay
                .Where(target => settingsByTarget.ContainsKey(target.Id))
                .Select(target => (Target: target, Settings: settingsByTarget[target.Id], Reach: reach.TooCloseReachOf(target, settingsByTarget[target.Id])))
                .Where(entry => bounds.Any(cause => cause.SpaceKey == entry.Target.SpaceKey && cause.Box.Overlaps(entry.Reach))),
        ];
        var checkedWorld = world.World with
        {
            Targets = [.. checkedTargets.Select((entry, position) => entry.Target with { Id = new TargetId(position) })],
            OtherModObjects = [.. copies],
            SupportOnlyObjects = [],
            Order = new WorkOrder([.. Enumerable.Range(0, checkedTargets.Count)]),
        };
        return new GroupTooCloseGuard(
            checkedWorld, [.. checkedTargets.Select(entry => entry.Settings)], [.. causes], [.. checkedTargets.Select(entry => entry.Reach)], reach, createRule, execution);
    }

    /// <summary>
    /// The candidates going back that a checked target comes too close to, each with the first such target in target order; where a
    /// target's check stops unexpectedly, every candidate going back its zones reach, with the failure. A target names the first
    /// candidate it is too close to, so one too close to several refuses one per pass, and the passes refuse the rest.
    /// </summary>
    /// <param name="endsWhereItGoesBack">Whether a candidate goes back and is present in the final world there.</param>
    public Dictionary<int, PlacedRefusal> Find(Func<int, bool> endsWhereItGoesBack)
    {
        if (world.Targets.IsEmpty) return [];
        var leftOut = world.OtherModObjects.Where(cause => !endsWhereItGoesBack(candidateOf[cause.Id.Index])).Select(cause => cause.Id).ToList();
        var rules = settingsOf.Distinct().ToDictionary(settings => settings, settings => createRule(world, leftOut, settings));
        var checks = ParallelMap.Run(
            execution, world.Targets.Length, position => rules[settingsOf[position]].CheckWhereItStands(world.Targets[position]), ParallelMap.AutomaticRangeSize);
        var first = new Dictionary<int, PlacedRefusal>();
        for (var position = 0; position < checks.Length; position++)
        {
            foreach (var (candidate, refusal) in RefusalsOf(position, checks[position], endsWhereItGoesBack)) first.TryAdd(candidate, refusal);
        }
        return first;
    }

    private IEnumerable<(int Candidate, PlacedRefusal Refusal)> RefusalsOf(int position, PlacementCheck check, Func<int, bool> endsWhereItGoesBack)
    {
        if (check.Failure is { } failure)
        {
            return world.OtherModObjects
                .Where(cause => endsWhereItGoesBack(candidateOf[cause.Id.Index]) && reach.BoundsOf(cause).Overlaps(reachOf[position]))
                .Select(cause => (candidateOf[cause.Id.Index], TooCloseRefusals.Stopped(failure)));
        }
        return TooCloseRefusals.ClashOf(check) is { } clash
            ? [(candidateOf[clash.Other.Index], TooCloseRefusals.ByWhatStays(world.Targets[position].Key))]
            : [];
    }

    /// <summary>
    /// Whether the candidate, once back, ends with a part only a version the removal steps do not fully believe holds: a part a later
    /// plugin that is not believed for its own changes left, or a part going back to an earlier version they do not fully believe.
    /// </summary>
    public static bool StaysACause(RecordChangedByCleanedMod change, AcceptedWorld accepted) =>
        change.Earlier.LeftToLaterPlugins.Values.Any(plugin => accepted.TrustOf(plugin) is not (VersionTrust.Full or VersionTrust.OwnEdits))
        || (change.Earlier.Undo != PlacedParts.None && accepted.TrustOf(change.Earlier.Plugin) != VersionTrust.Full);
}
