using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The report records of the removal decisions: removals and kept objects, round by round.</summary>
internal static class RemovalList
{
    /// <summary>The objects with the reasons that took them from where the cleaned mod put them, in the order given: the order the patch was written in.</summary>
    public static List<ObjectWithReason> WithReasons(
        IEnumerable<TargetId> inWriteOrder, IRemovalDecisions decisions, CollectedObjects world, VisibleLeftBehindResult visibleLeftBehind, LeftBehindResult leftBehind) =>
        WithReasons(inWriteOrder, decisions, world, ChecksOf(visibleLeftBehind, leftBehind));

    /// <summary>The objects of a round that holds no left-behind object, with their reasons.</summary>
    public static IEnumerable<ObjectWithReason> WithReasons(IEnumerable<TargetId> inRound, IRemovalDecisions decisions, CollectedObjects world) =>
        WithReasons(inRound, decisions, world, NoChecks);

    /// <summary>The reason an old-place check found a collision, worded from the world the check was made in.</summary>
    public static ObjectWithReason Collision(TargetId target, RemovalReason reason, CollectedObjects oldPlaceWorld) =>
        ToObjectWithReason(target, reason, oldPlaceWorld, NoChecks);

    /// <summary>Every kept object: round by round, in target order.</summary>
    public static List<KeptObject> Kept(IRemovalDecisions decisions) => [.. decisions.Rounds.SelectMany(round => KeptIn(decisions, round))];

    public static IEnumerable<KeptObject> KeptIn(IRemovalDecisions decisions, Round round) =>
        decisions.KeptIn(round).Select(target => ToKept(decisions, target, (Decision.Kept)decisions.Of(target)!));

    /// <summary>How many of the objects are restored.</summary>
    public static int CountRestored(IRemovalDecisions decisions, IEnumerable<TargetId> targets) => targets.Count(target => decisions.Of(target) is Decision.Restored);

    /// <summary>How many objects the round disabled as members of a linked group.</summary>
    public static int CountLinkedIn(IRemovalDecisions decisions, Round round) => CountAsLinked(decisions, decisions.RemovedIn(round));

    /// <summary>How many objects the round restored as members of a linked group.</summary>
    public static int CountRestoredLinkedIn(IRemovalDecisions decisions, Round round) => CountAsLinked(decisions, decisions.RestoredIn(round));

    /// <summary>How many objects the round disabled as members of a linked group because a member of it was restored.</summary>
    public static int CountLinkedToRestoredIn(IRemovalDecisions decisions, Round round) =>
        decisions.RemovedIn(round).Count(target => RoundEffects.FollowedARestoredMember(decisions.Of(target), decisions.Of));

    private static int CountAsLinked(IRemovalDecisions decisions, IEnumerable<TargetId> targets) =>
        targets.Count(target => RoundEffects.Of(decisions.Of(target)).AsLinked);

    private static List<ObjectWithReason> WithReasons(IEnumerable<TargetId> targets, IRemovalDecisions decisions, CollectedObjects world, LeftBehindChecks checks) =>
        [.. targets.Select(target => ToObjectWithReason(target, decisions.Of(target)!.Reason, world, checks))];

    private static LeftBehindChecks ChecksOf(VisibleLeftBehindResult visibleLeftBehind, LeftBehindResult leftBehind) =>
        new(VisibleLeftBehindByTarget(visibleLeftBehind.Evaluations), LeftBehindByTarget(leftBehind.Evaluations));

    private static readonly LeftBehindChecks NoChecks = new(VisibleLeftBehindByTarget([]), LeftBehindByTarget([]));

    private static PerIndexTable<VisibleLeftBehindCheck> VisibleLeftBehindByTarget(IEnumerable<VisibleLeftBehindCheck> evaluations) =>
        PerIndexTable<VisibleLeftBehindCheck>.From(evaluations, evaluation => evaluation.TargetIndex);

    private static PerIndexTable<LeftBehindCheck> LeftBehindByTarget(IEnumerable<LeftBehindCheck> evaluations) =>
        PerIndexTable<LeftBehindCheck>.From(evaluations, evaluation => evaluation.TargetIndex);

    private static ObjectWithReason ToObjectWithReason(TargetId target, RemovalReason reason, CollectedObjects world, LeftBehindChecks checks) => reason switch
    {
        RemovalReason.TerrainChanged changed => new ObjectWithReason.TerrainChanged(target.Index, changed.ChangedBy, changed.Kind),
        RemovalReason.TooClose tooClose => new ObjectWithReason.TooClose(target.Index, world.OtherModObjects[tooClose.Other.Index], tooClose.Cause),
        RemovalReason.Touching touching => new ObjectWithReason.Touching(target.Index, touching.Touched.Index),
        RemovalReason.LostSupport lost => new ObjectWithReason.LostSupport(target.Index, lost.LeftItsPlaceShare, lost.RestoredShare, lost.MainSupporter.Index),
        RemovalReason.TouchingAnotherModsObject touching => new ObjectWithReason.TouchingAnotherModsObject(target.Index, world.Other(touching.Touched)),
        RemovalReason.LostSupportWithAnotherModsObject lost =>
            new ObjectWithReason.LostSupportWithAnotherModsObject(target.Index, lost.LeftItsPlaceShare, lost.RestoredShare, world.Other(lost.MainSupporter)),
        RemovalReason.VisibleObjectSurroundingsRemoved => new ObjectWithReason.VisibleLeftBehind(target.Index, checks.Visible.Get(target.Index)),
        RemovalReason.InsideAnotherModsObject or RemovalReason.SurroundingsRemoved => new ObjectWithReason.LeftBehind(target.Index, checks.Invisible.Get(target.Index)),
        RemovalReason.LinkedTo linked => new ObjectWithReason.LinkedTo(target.Index, linked.To.Index),
        _ => throw new InvalidOperationException($"Unknown removal reason {reason}."),
    };

    /// <summary>The left-behind steps' checks of the removed objects, by target.</summary>
    private sealed record LeftBehindChecks(PerIndexTable<VisibleLeftBehindCheck> Visible, PerIndexTable<LeftBehindCheck> Invisible);

    private static KeptObject ToKept(IRemovalDecisions decisions, TargetId target, Decision.Kept kept)
    {
        var touched = TouchedTargetThatLeftItsPlace(kept.Reason);
        return new(target.Index, kept.KeepReason, touched, touched is { } index ? decisions.Of(new TargetId(index)) : null);
    }

    private static int? TouchedTargetThatLeftItsPlace(RemovalReason reason) => reason switch
    {
        RemovalReason.Touching touching => touching.Touched.Index,
        RemovalReason.LostSupport lost => lost.MainSupporter.Index,
        _ => null,
    };
}
