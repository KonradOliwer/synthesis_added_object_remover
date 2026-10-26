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
    /// <summary>Every removal, in the order given: the order the patch was written in.</summary>
    public static List<RemovedObject> Removals(
        IEnumerable<TargetId> inWriteOrder, IRemovalDecisions decisions, CollectedObjects world, VisibleLeftBehindResult visibleLeftBehind, LeftBehindResult leftBehind)
    {
        var checks = new LeftBehindChecks(VisibleLeftBehindByTarget(visibleLeftBehind.Evaluations), LeftBehindByTarget(leftBehind.Evaluations));
        return [.. inWriteOrder.Select(target => ToRemoval(target, decisions.Of(target)!.Reason, world, checks))];
    }

    /// <summary>The removals of a round that holds no left-behind removal.</summary>
    public static IEnumerable<RemovedObject> RemovalsIn(IRemovalDecisions decisions, CollectedObjects world, Round round) =>
        RemovalsIn(decisions, world, new LeftBehindChecks(VisibleLeftBehindByTarget([]), LeftBehindByTarget([])), round);

    /// <summary>Every kept object: round by round, in target order.</summary>
    public static List<KeptObject> Kept(IRemovalDecisions decisions) => [.. decisions.Rounds.SelectMany(round => KeptIn(decisions, round))];

    public static IEnumerable<KeptObject> KeptIn(IRemovalDecisions decisions, Round round) =>
        decisions.KeptIn(round).Select(target => ToKept(target, (Decision.Kept)decisions.Of(target)!));

    public static int CountLinkedIn(IRemovalDecisions decisions, Round round) =>
        decisions.RemovedIn(round).Count(target => decisions.Of(target)!.Reason is RemovalReason.LinkedTo);

    private static PerIndexTable<VisibleLeftBehindCheck> VisibleLeftBehindByTarget(IEnumerable<VisibleLeftBehindCheck> evaluations) =>
        PerIndexTable<VisibleLeftBehindCheck>.From(evaluations, evaluation => evaluation.TargetIndex);

    private static PerIndexTable<LeftBehindCheck> LeftBehindByTarget(IEnumerable<LeftBehindCheck> evaluations) =>
        PerIndexTable<LeftBehindCheck>.From(evaluations, evaluation => evaluation.TargetIndex);

    private static IEnumerable<RemovedObject> RemovalsIn(
        IRemovalDecisions decisions, CollectedObjects world, LeftBehindChecks checks, Round round) =>
        decisions.RemovedIn(round).Select(target => ToRemoval(target, decisions.Of(target)!.Reason, world, checks));

    private static RemovedObject ToRemoval(TargetId target, RemovalReason reason, CollectedObjects world, LeftBehindChecks checks) => reason switch
    {
        RemovalReason.TerrainChanged changed => new TerrainChangedRemoval(target.Index, changed.ChangedBy, changed.Kind),
        RemovalReason.TooClose tooClose => new TooCloseRemoval(target.Index, world.OtherModObjects[tooClose.Other.Index], tooClose.Cause),
        RemovalReason.Touching touching => new TouchingRemoval(target.Index, touching.Touched.Index),
        RemovalReason.LostSupport lost => new SupportRemoval(target.Index, lost.RemovedShare, lost.MainSupporter.Index),
        RemovalReason.TouchingAnotherModsObject touching => new TouchingAnotherModsObjectRemoval(target.Index, world.Other(touching.Touched)),
        RemovalReason.LostSupportWithAnotherModsObject lost => new SupportWithAnotherModsObjectRemoval(target.Index, lost.RemovedShare, world.Other(lost.MainSupporter)),
        RemovalReason.VisibleObjectSurroundingsRemoved => new VisibleLeftBehindRemoval(target.Index, checks.Visible.Get(target.Index)),
        RemovalReason.InsideAnotherModsObject or RemovalReason.SurroundingsRemoved => new LeftBehindRemoval(target.Index, checks.Invisible.Get(target.Index)),
        RemovalReason.LinkedTo linked => new LinkedRemoval(target.Index, linked.To.Index),
        _ => throw new InvalidOperationException($"Unknown removal reason {reason}."),
    };

    /// <summary>The left-behind steps' checks of the removed objects, by target.</summary>
    private sealed record LeftBehindChecks(PerIndexTable<VisibleLeftBehindCheck> Visible, PerIndexTable<LeftBehindCheck> Invisible);

    private static KeptObject ToKept(TargetId target, Decision.Kept kept) =>
        new(target.Index, kept.KeepReason, TouchedRemovedTarget(kept.Reason));

    private static int? TouchedRemovedTarget(RemovalReason reason) => reason switch
    {
        RemovalReason.Touching touching => touching.Touched.Index,
        RemovalReason.LostSupport lost => lost.MainSupporter.Index,
        _ => null,
    };
}
