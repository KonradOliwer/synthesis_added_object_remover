using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The report records of the removal decisions: removals and kept objects, round by round.</summary>
internal static class RemovalList
{
    /// <summary>Every removal: round by round, each round's direct removals in target order, then the linked ones.</summary>
    public static List<RemovedObject> Removals(IRemovalDecisions decisions, CollectedObjects world, LeftBehindResult leftBehind)
    {
        var leftBehindByTarget = LeftBehindByTarget(leftBehind);
        return [.. decisions.Rounds.SelectMany(round => RemovalsIn(decisions, world, leftBehindByTarget, round))];
    }

    public static IEnumerable<RemovedObject> RemovalsIn(IRemovalDecisions decisions, CollectedObjects world, LeftBehindResult leftBehind, Round round) =>
        RemovalsIn(decisions, world, LeftBehindByTarget(leftBehind), round);

    /// <summary>Every kept object: round by round, in target order.</summary>
    public static List<KeptObject> Kept(IRemovalDecisions decisions) => [.. decisions.Rounds.SelectMany(round => KeptIn(decisions, round))];

    public static IEnumerable<KeptObject> KeptIn(IRemovalDecisions decisions, Round round) =>
        decisions.KeptIn(round).Select(target => ToKept(target, (Decision.Kept)decisions.Of(target)!));

    public static int CountLinkedIn(IRemovalDecisions decisions, Round round) =>
        decisions.RemovedIn(round).Count(target => decisions.Of(target)!.Reason is RemovalReason.LinkedTo);

    private static PerIndexTable<LeftBehindCheck> LeftBehindByTarget(LeftBehindResult leftBehind) =>
        PerIndexTable<LeftBehindCheck>.From(leftBehind.Evaluations, evaluation => evaluation.TargetIndex);

    private static IEnumerable<RemovedObject> RemovalsIn(
        IRemovalDecisions decisions, CollectedObjects world, PerIndexTable<LeftBehindCheck> leftBehindByTarget, Round round) =>
        decisions.RemovedIn(round).Select(target => ToRemoval(target, decisions.Of(target)!.Reason, world, leftBehindByTarget));

    private static RemovedObject ToRemoval(
        TargetId target, RemovalReason reason, CollectedObjects world, PerIndexTable<LeftBehindCheck> leftBehindByTarget) => reason switch
    {
        RemovalReason.TooClose tooClose => new TooCloseRemoval(target.Index, world.OtherModObjects[tooClose.Other.Index]),
        RemovalReason.Touching touching => new TouchingRemoval(target.Index, touching.Touched.Index),
        RemovalReason.LostSupport lost => new AnchoringRemoval(target.Index, lost.RemovedShare, lost.MainSupporter.Index),
        RemovalReason.InsideAnotherModsObject or RemovalReason.SurroundingsRemoved => new LeftBehindRemoval(target.Index, leftBehindByTarget.Get(target.Index)),
        RemovalReason.LinkedTo linked => new LinkedRemoval(target.Index, linked.To.Index),
        _ => throw new InvalidOperationException($"Unknown removal reason {reason}."),
    };

    private static KeptObject ToKept(TargetId target, Decision.Kept kept) =>
        new(target.Index, kept.KeepReason, TouchedRemovedTarget(kept.Reason));

    private static int? TouchedRemovedTarget(RemovalReason reason) => reason switch
    {
        RemovalReason.Touching touching => touching.Touched.Index,
        RemovalReason.LostSupport lost => lost.MainSupporter.Index,
        _ => null,
    };
}
