using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// What the copy rule reads about another mod's object the cleaned mod moved: whether a copy of it exists, whether the cleaned mod's
/// standing objects hold it as a left-behind neighbour, and whether it may be copied. Thread-safe.
/// </summary>
/// <param name="cloned">The originals that a copy for the cleaned mod read at scan time names.</param>
/// <param name="neighboursOfStandingObjects">The targets a switched-on left-behind step measures around one of the cleaned mod's objects that stands.</param>
internal sealed class CloneCandidates(IRunCaches caches, IReadOnlySet<RecordKey> cloned, IReadOnlySet<TargetId> neighboursOfStandingObjects)
{
    /// <param name="leftBehind">The neighbours of each left-behind step that is switched on.</param>
    /// <param name="candidates">The placed records the restore judges.</param>
    public static CloneCandidates Of(
        CollectedObjects collected,
        IRemovalDecisions final,
        ImmutableArray<ILeftBehindNeighbours> leftBehind,
        IRunCaches caches,
        Execution execution,
        IReadOnlyList<RecordChangedByCleanedMod> candidates)
    {
        var cloned = collected.Targets.Select(target => target.CloneOf).OfType<RecordKey>().ToHashSet();
        var anyMoved = candidates.Any(change => IsMovedObject(change, collected.Targets));
        return new CloneCandidates(caches, cloned, anyMoved ? NeighboursOfStandingObjects(collected.Targets, final, leftBehind, execution) : []);
    }

    /// <summary>Another mod's object the cleaned mod moved, whose position goes back: a base swap in place, a turn or a rescale only is none.</summary>
    public static bool IsMovedObject(RecordChangedByCleanedMod change, IReadOnlyList<TargetObject> targets) =>
        change.Target is { } id && targets[id.Index].Origin == TargetOrigin.Moved && change.Earlier.Undo.HasFlag(PlacedParts.Position);

    /// <summary>The copy of a moved object as a later step reads it: one of the cleaned mod's objects, where the object stands.</summary>
    public static TargetObject AsClone(TargetObject standing) => standing with { Origin = TargetOrigin.CleanedMod, CloneOf = standing.Key };

    public bool HasClone(RecordChangedByCleanedMod change) => cloned.Contains(change.Key);

    public bool IsNeighbourOfStandingObject(TargetId target) => neighboursOfStandingObjects.Contains(target);

    /// <returns>Why the record the game shows now may not be copied; null when it may.</returns>
    /// <remarks>A target is never deleted or initially disabled: the scan makes no target of such a record.</remarks>
    public CloneRefusal? RefusalOf(RecordChangedByCleanedMod change, TargetObject target)
    {
        if (target.Base is not { } baseKey || !caches.Shapes.VisibilityOf(target).IsVisible || caches.Bases.Of(baseKey) is not { Resolved: true, IsPlainScenery: true } facts)
        {
            return CloneRefusal.NotPlainScenery;
        }

        if (facts.HasDistantLod || change.ReferenceNow.IsVisibleWhenDistant) return CloneRefusal.HasDistantLod;
        if (change.IsPersistent) return CloneRefusal.Persistent;
        return change.ReferenceNow.HoldsOnlyPlacement ? null : CloneRefusal.HoldsMoreThanPlacement;
    }

    /// <summary>The targets a left-behind step measures around one of the cleaned mod's objects that has not left its place, where the rounds measure it.</summary>
    private static HashSet<TargetId> NeighboursOfStandingObjects(
        ImmutableArray<TargetObject> targets, IRemovalDecisions final, ImmutableArray<ILeftBehindNeighbours> leftBehind, Execution execution)
    {
        TargetObject[] standing = [.. final.TargetsAsTheRoundsMeasureThem(targets).Where(target => target.Origin == TargetOrigin.CleanedMod && !final.HasLeftItsPlace(target.Id))];
        return
        [
            .. leftBehind.SelectMany(step => ParallelMap.Run(execution, standing.Length, index => NeighboursOf(step, standing[index]), ParallelMap.AutomaticRangeSize))
                .SelectMany(neighbours => neighbours),
        ];
    }

    private static List<TargetId> NeighboursOf(ILeftBehindNeighbours step, TargetObject target)
    {
        List<TargetId> neighbours = [];
        step.Around(target, neighbours);
        return neighbours;
    }
}
