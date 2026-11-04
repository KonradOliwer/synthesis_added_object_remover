using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The too-close step's check of planned copies as the cleaned mod's own objects, with the too-close settings of those objects,
/// against every object that can cause removals in a later step: the other mods' objects the removal steps judge by that stand
/// where they end (one the patch pulled down where it lands, one it removed or one going back left out), and every placed record
/// going back that stays a cause once back, as it goes back. Thread-safe.
/// </summary>
/// <param name="causes">Every such object, in that order; the ids are not used.</param>
/// <param name="createRule">The too-close rule over a world of what stays with the given causes left out and the given settings.</param>
internal sealed class CloneTooCloseCheck(
    ImmutableArray<OtherObject> causes,
    CollectedObjects collected,
    GuardReach reach,
    TooCloseSettings settings,
    Func<CollectedObjects, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> createRule,
    Execution execution)
{
    /// <param name="goingBack">The placed records whose areas let them go back.</param>
    /// <param name="settings">The too-close settings of the cleaned mod's own objects.</param>
    public static CloneTooCloseCheck Of(
        CollectedObjects collected,
        OtherModsObjectsResult otherModsObjects,
        IReadOnlyList<RecordChangedByCleanedMod> goingBack,
        AcceptedWorld accepted,
        PluginName cleanedMod,
        GuardReach reach,
        TooCloseSettings settings,
        Func<CollectedObjects, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> createRule,
        Execution execution)
    {
        var takenAway = otherModsObjects.TakenAway();
        var leaving = goingBack.Select(change => change.Key).ToHashSet();
        var others = collected.OtherModObjects
            .Where(other => !leaving.Contains(other.Key))
            .Select(other => takenAway.TryGetValue(other.Id, out var after) ? after : other)
            .OfType<OtherObject>();
        var candidates = goingBack
            .Where(change => GroupTooCloseGuard.StaysACause(change, accepted))
            .Select(change => AfterRemovalWorld.AsItIsNow(change, default, collected.Targets).RestoredAs(change.Earlier).AsOtherObject(default, cleanedMod));
        return new CloneTooCloseCheck([.. others.Concat(candidates)], collected, reach, settings, createRule, execution);
    }

    /// <param name="clones">The copies where they stand, as the cleaned mod's objects, with their position as id.</param>
    /// <returns>The check of each copy, by its position.</returns>
    public PlacementCheck[] Check(IReadOnlyList<TargetObject> clones)
    {
        var reachOf = clones.Select(clone => (clone.SpaceKey, Box: reach.TooCloseReachOf(clone, settings))).ToList();
        List<OtherObject> near =
        [
            .. causes
                .Where(cause => cause.IsPlacedNpc || reachOf.Any(clone => clone.SpaceKey == cause.SpaceKey && clone.Box.Overlaps(reach.BoundsOf(cause))))
                .Select((cause, index) => cause with { Id = new OtherId(index) }),
        ];
        var world = collected with
        {
            Targets = [.. clones],
            OtherModObjects = [.. near],
            SupportOnlyObjects = [],
            Order = new WorkOrder([.. Enumerable.Range(0, clones.Count)]),
        };
        var rule = createRule(world, [], settings);
        return ParallelMap.Run(execution, clones.Count, position => rule.CheckWhereItStands(clones[position]), ParallelMap.AutomaticRangeSize);
    }
}
