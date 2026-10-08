using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <param name="Hits">In target order.</param>
/// <param name="Failures">The targets whose check failed, in target order.</param>
internal sealed record TooCloseSearchResult(List<TooCloseObject> Hits, TooCloseWork Work, ImmutableArray<TargetFailure> Failures);

/// <summary>
/// BoundingBox removal zone: finds visible target objects whose grown (multiplier-expanded) local
/// box contains the bounds center of some other-mod object that can cause removals. Other mods' placed NPCs follow the
/// <see cref="NpcTooCloseRule"/>. Invisible targets are left to the leftover invisible objects step.
/// </summary>
internal static class TooCloseSearch
{
    /// <summary>Reusable buffers and counters of one worker thread.</summary>
    private sealed class Scratch
    {
        public ObjectQueryScratch Query { get; } = new();
        public NpcScratch Npcs { get; } = new();

        public TooCloseWork Harvest() => new(ShapeZoneWork.Zero, Npcs.Harvest());
    }

    /// <summary>Candidates are tested in index order, so which other-mod object is reported does not depend on thread scheduling.</summary>
    public static TooCloseSearchResult FindTooCloseTargets(
        IReadOnlyList<TargetObject> targets,
        IObjectsThatCanCauseRemovals otherModObjects,
        IBaseObjectShapes shapes,
        float multiplier,
        NpcTooCloseRule npcRule,
        WorkOrder order,
        Execution execution)
    {
        return FindForVisibleTargets(
            targets,
            otherModObjects,
            shapes,
            order,
            execution,
            () => new Scratch(),
            (targetIndex, scratch) => FindFirstTooCloseOther(targets[targetIndex], targetIndex, otherModObjects, shapes, multiplier, npcRule, scratch),
            scratch => scratch.Harvest());
    }

    /// <summary>The outcome of checking one target: the other-mod object it is too close to, or the failure that stopped the check.</summary>
    private readonly record struct TargetCheck(OtherId? TooCloseTo, string? Failure);

    /// <summary>Runs the search for every visible target on worker threads; each worker has its own scratch.</summary>
    public static TooCloseSearchResult FindForVisibleTargets<TScratch>(
        IReadOnlyList<TargetObject> targets,
        IObjectsThatCanCauseRemovals otherModObjects,
        IBaseObjectShapes shapes,
        WorkOrder order,
        Execution execution,
        Func<TScratch> createScratch,
        Func<int, TScratch, OtherId?> findFirstTooCloseOther,
        Func<TScratch, TooCloseWork> harvest)
    {
        var (checks, work) = ParallelMap.Run(
            execution,
            order,
            targets.Count,
            createScratch,
            (targetIndex, scratch) => CheckTarget(targets[targetIndex], targetIndex, shapes, scratch, findFirstTooCloseOther),
            harvest,
            ParallelMap.AutomaticRangeSize);
        return new TooCloseSearchResult(ToHits(otherModObjects, checks), work, ToFailures(checks));
    }

    /// <remarks>A target whose check fails is not too close to anything, so it is not removed.</remarks>
    private static TargetCheck CheckTarget<TScratch>(
        TargetObject target, int targetIndex, IBaseObjectShapes shapes, TScratch scratch, Func<int, TScratch, OtherId?> findFirstTooCloseOther)
    {
        try
        {
            return new TargetCheck(shapes.VisibilityOf(target).IsVisible ? findFirstTooCloseOther(targetIndex, scratch) : null, Failure: null);
        }
        catch (Exception ex) when (Failures.IsRecoverable(ex))
        {
            return new TargetCheck(TooCloseTo: null, Failures.Describe(ex));
        }
    }

    /// <returns>The hits in target order.</returns>
    private static List<TooCloseObject> ToHits(IObjectsThatCanCauseRemovals otherModObjects, IReadOnlyList<TargetCheck> checks)
    {
        var hits = new List<TooCloseObject>();
        for (var target = 0; target < checks.Count; target++)
        {
            if (checks[target].TooCloseTo is { } otherModObject) hits.Add(new TooCloseObject(target, otherModObjects.Get(otherModObject)));
        }
        return hits;
    }

    private static ImmutableArray<TargetFailure> ToFailures(IReadOnlyList<TargetCheck> checks) =>
    [
        .. Enumerable.Range(0, checks.Count).Where(target => checks[target].Failure != null).Select(target => new TargetFailure(target, checks[target].Failure!)),
    ];

    private static OtherId? FindFirstTooCloseOther(
        TargetObject target, int targetIndex, IObjectsThatCanCauseRemovals otherModObjects, IBaseObjectShapes shapes, float multiplier, NpcTooCloseRule npcRule, Scratch scratch) =>
        npcRule.ThenFirstStuckNpc(
            FindFirstCentreInBoxZone(target, otherModObjects, shapes, multiplier, scratch.Query), targetIndex, scratch.Npcs);

    /// <summary>The lowest other-mod object that can cause removals whose bounds centre lies in the target's BoundingBox zone, or null.</summary>
    public static OtherId? FindFirstCentreInBoxZone(
        TargetObject target,
        IObjectsThatCanCauseRemovals otherModObjects,
        IBaseObjectShapes shapes,
        float multiplier,
        ObjectQueryScratch scratch)
    {
        var expanded = Boxes.ExpandedLocalBox(shapes.Of(target.Base).Box, target.Transform.Scale, multiplier);
        var position = target.Transform.Position;
        var rotation = target.Transform.Rotation;
        return otherModObjects.FirstWithCentreInside(
            target.SpaceKey,
            Boxes.WorldAabb(expanded, position, rotation),
            centre => Boxes.IsInsideOrientedBox(centre, position, rotation, expanded),
            scratch);
    }
}
