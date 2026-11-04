using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The too-close step's check of copies as the cleaned mod's own objects, with the too-close settings of those objects, against
/// every object that can cause removals in a later step once the given candidates go back: the other mods' objects the removal
/// steps judge by and the targets a removal step restored to their earlier version, that stand where they end (one the patch pulled
/// down where it lands; one it removed, one going back and one that ends removed left out), and every candidate going back that stays
/// a cause once back and ends there, as it goes back. Thread-safe.
/// </summary>
/// <param name="others">
/// The other mods' objects the removal steps judge by, where they stand once the patch pulled some down, the removed ones left out,
/// then the targets a removal step restored, where they stand last.
/// </param>
/// <param name="candidateCauses">By candidate index: the candidate as it goes back, when it stays a cause once back; null otherwise. The ids are not used.</param>
/// <param name="createRule">The too-close rule over a world of what stays with the given causes left out and the given settings.</param>
internal sealed class CloneTooCloseCheck(
    ImmutableArray<OtherObject> others,
    ImmutableArray<OtherObject?> candidateCauses,
    AfterRemovalWorld world,
    FinalPresence presence,
    GuardReach reach,
    TooCloseSettings settings,
    Func<CollectedObjects, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> createRule,
    Execution execution)
{
    /// <param name="candidates">The placed records whose areas let them go back, in candidate order.</param>
    /// <param name="settings">The too-close settings of the cleaned mod's own objects.</param>
    /// <param name="final">The decisions with every round and every move, which tell the targets a removal step restored.</param>
    public static CloneTooCloseCheck Of(
        CollectedObjects collected,
        IRemovalDecisions final,
        OtherModsObjectsResult otherModsObjects,
        AfterRemovalWorld world,
        IReadOnlyList<RecordChangedByCleanedMod> candidates,
        AcceptedWorld accepted,
        PluginName cleanedMod,
        FinalPresence presence,
        GuardReach reach,
        TooCloseSettings settings,
        Func<CollectedObjects, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> createRule,
        Execution execution)
    {
        var takenAway = otherModsObjects.TakenAway();
        var restoredByRemovalSteps = AfterRemovalWorld.TargetsWhereTheyStandLast(collected.Targets, final)
            .Where(target => final.Of(target.Id) is Decision.Restored)
            .Select(target => target.AsOtherObject(default, cleanedMod));
        ImmutableArray<OtherObject> others =
            [.. collected.OtherModObjects.Select(other => takenAway.TryGetValue(other.Id, out var after) ? after : other).OfType<OtherObject>().Concat(restoredByRemovalSteps)];
        ImmutableArray<OtherObject?> causes =
            [.. candidates.Select((change, index) => GroupTooCloseGuard.StaysACause(change, accepted) ? world.Restored[index].AsOtherObject(default, cleanedMod) : (OtherObject?)null)];
        return new CloneTooCloseCheck(others, causes, world, presence, reach, settings, createRule, execution);
    }

    /// <param name="clones">The copies where they stand, as the cleaned mod's objects, with their position as id.</param>
    /// <param name="goingBack">The indexes of the candidates going back.</param>
    /// <returns>The check of each copy, by its position.</returns>
    public PlacementCheck[] Check(IReadOnlyList<TargetObject> clones, IReadOnlySet<int> goingBack)
    {
        var reachOf = clones.Select(clone => (clone.SpaceKey, Box: reach.TooCloseReachOf(clone, settings))).ToList();
        List<OtherObject> near =
        [
            .. CausesOnceBack(goingBack)
                .Where(cause => cause.IsPlacedNpc || reachOf.Any(clone => clone.SpaceKey == cause.SpaceKey && clone.Box.Overlaps(reach.BoundsOf(cause))))
                .Select((cause, index) => cause with { Id = new OtherId(index) }),
        ];
        var checkedWorld = world.World with
        {
            Targets = [.. clones],
            OtherModObjects = [.. near],
            SupportOnlyObjects = [],
            Order = new WorkOrder([.. Enumerable.Range(0, clones.Count)]),
        };
        var rule = createRule(checkedWorld, [], settings);
        return ParallelMap.Run(execution, clones.Count, position => rule.CheckWhereItStands(clones[position]), ParallelMap.AutomaticRangeSize);
    }

    /// <summary>The causes once the given candidates go back, in the order of the class remarks.</summary>
    private IEnumerable<OtherObject> CausesOnceBack(IReadOnlySet<int> goingBack)
    {
        var leaving = goingBack.Select(candidate => world.Restored[candidate].Key).ToHashSet();
        return others
            .Where(other => !leaving.Contains(other.Key) && !presence.EndsRemoved(other.Key, goingBack.Contains))
            .Concat(goingBack.Order()
                .Where(candidate => !presence.EndsRemoved(world.Restored[candidate].Key, goingBack.Contains))
                .Select(candidate => candidateCauses[candidate])
                .OfType<OtherObject>());
    }
}
