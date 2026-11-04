using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
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

    /// <summary>The world of the first check, which every later check reuses; null before it.</summary>
    /// <remarks>
    /// The world's caches live as long as the run, so one world serves every pass: it holds the copies of the first check and every
    /// object that can be a cause within their reach, and each check leaves out what is no cause then. Copies only get lost between
    /// passes, so the first check's copies and their reach hold those of every later one.
    /// </remarks>
    private CheckedWorld? _checked;

    private readonly Lock _building = new();

    /// <param name="clones">The copies where they stand, as the cleaned mod's objects, with their original as <see cref="TargetObject.CloneOf"/>.</param>
    /// <param name="goingBack">The indexes of the candidates going back.</param>
    /// <returns>The check of each copy, in the order given.</returns>
    public PlacementCheck[] Check(IReadOnlyList<TargetObject> clones, IReadOnlySet<int> goingBack)
    {
        var checkedWorld = WorldFor(clones);
        var causes = CausesOnceBack(goingBack).ToHashSet();
        var leftOut = checkedWorld.Causes.Where(entry => !causes.Contains(entry.Cause)).Select(entry => entry.Id);
        var rule = createRule(checkedWorld.World, leftOut, settings);
        return ParallelMap.Run(execution, clones.Count, position => rule.CheckWhereItStands(checkedWorld.TargetOf(clones[position])), ParallelMap.AutomaticRangeSize);
    }

    /// <summary>The world of the first check; a later check's copies must be among its copies.</summary>
    private CheckedWorld WorldFor(IReadOnlyList<TargetObject> clones)
    {
        lock (_building)
        {
            _checked ??= BuildWorld(clones);
            return _checked;
        }
    }

    /// <summary>The copies, with their position as id, and every object that can be a cause within their too-close reach, with its position among them as id.</summary>
    private CheckedWorld BuildWorld(IReadOnlyList<TargetObject> clones)
    {
        TargetObject[] targets = [.. clones.Select((clone, position) => clone with { Id = new TargetId(position) })];
        var reachOf = targets.Select(clone => (clone.SpaceKey, Box: reach.TooCloseReachOf(clone, settings))).ToList();
        List<OtherObject> near =
        [
            .. others.Concat(candidateCauses.OfType<OtherObject>())
                .Where(cause => cause.IsPlacedNpc || reachOf.Any(clone => clone.SpaceKey == cause.SpaceKey && clone.Box.Overlaps(reach.BoundsOf(cause)))),
        ];
        var causes = near.Select((cause, index) => (Cause: cause, Id: new OtherId(index))).ToList();
        var checkedWorld = world.World with
        {
            Targets = [.. targets],
            OtherModObjects = [.. causes.Select(entry => entry.Cause with { Id = entry.Id })],
            SupportOnlyObjects = [],
            Order = new WorkOrder([.. Enumerable.Range(0, targets.Length)]),
        };
        return new CheckedWorld(checkedWorld, causes, targets.ToDictionary(target => target.CloneOf!.Value));
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

    /// <param name="Causes">Every object of <paramref name="World"/> that can be a cause, as read, with its id there.</param>
    /// <param name="TargetsByOriginal">The copies of <paramref name="World"/> by their original.</param>
    private sealed record CheckedWorld(CollectedObjects World, List<(OtherObject Cause, OtherId Id)> Causes, Dictionary<RecordKey, TargetObject> TargetsByOriginal)
    {
        /// <summary>The world's copy of the given copy.</summary>
        public TargetObject TargetOf(TargetObject clone) =>
            TargetsByOriginal.TryGetValue(clone.CloneOf!.Value, out var target)
                ? target
                : throw new InvalidOperationException($"The copy of {clone.CloneOf} is checked although the first check had no copy of it.");
    }
}
