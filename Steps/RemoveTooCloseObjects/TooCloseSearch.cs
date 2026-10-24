using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <param name="Hits">In target order.</param>
/// <param name="Failures">The targets whose check failed, in target order.</param>
internal sealed record TooCloseSearchResult(List<TooCloseObject> Hits, TooCloseWork Work, ImmutableArray<PartFailure> Failures);

/// <summary>The outcome of checking one target: the other-mod object it is too close to, or the failure that stopped the check.</summary>
internal readonly record struct TargetCheck(ZoneHit? TooCloseTo, string? Failure);

/// <summary>
/// BoundingBox removal zone: finds visible target objects whose grown local box (grown by a multiple of
/// its size or by a distance in game units, see <see cref="ZoneGrowth.GrownScaledBox"/>) contains the bounds center of
/// some other-mod object that can cause removals. Other mods' placed NPCs follow the
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
        ImmutableArray<ZoneGrowth> zones,
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
            (target, scratch) => FindFirstTooCloseOther(target, otherModObjects, shapes, zones, npcRule, scratch),
            scratch => scratch.Harvest());
    }

    /// <summary>One target alone, with a scratch of its own, so its work is not counted.</summary>
    public static TargetCheck CheckAlone(
        TargetObject target, IObjectsThatCanCauseRemovals otherModObjects, IBaseObjectShapes shapes, ImmutableArray<ZoneGrowth> zones, NpcTooCloseRule npcRule) =>
        CheckTarget(target, shapes, new Scratch(), (placed, scratch) => FindFirstTooCloseOther(placed, otherModObjects, shapes, zones, npcRule, scratch));

    /// <summary>Runs the search for every visible target on worker threads; each worker has its own scratch.</summary>
    public static TooCloseSearchResult FindForVisibleTargets<TScratch>(
        IReadOnlyList<TargetObject> targets,
        IObjectsThatCanCauseRemovals otherModObjects,
        IBaseObjectShapes shapes,
        WorkOrder order,
        Execution execution,
        Func<TScratch> createScratch,
        Func<TargetObject, TScratch, ZoneHit?> findFirstTooCloseOther,
        Func<TScratch, TooCloseWork> harvest)
    {
        var (checks, work) = ParallelMap.Run(
            execution,
            order,
            targets.Count,
            createScratch,
            (targetIndex, scratch) => CheckTarget(targets[targetIndex], shapes, scratch, findFirstTooCloseOther),
            harvest,
            ParallelMap.AutomaticRangeSize);
        return new TooCloseSearchResult(ToHits(otherModObjects, checks), work, ToFailures(targets, checks));
    }

    /// <remarks>A target whose check fails is not too close to anything, so it is not removed.</remarks>
    public static TargetCheck CheckTarget<TScratch>(
        TargetObject target, IBaseObjectShapes shapes, TScratch scratch, Func<TargetObject, TScratch, ZoneHit?> findFirstTooCloseOther)
    {
        try
        {
            return new TargetCheck(shapes.VisibilityOf(target).IsVisible ? findFirstTooCloseOther(target, scratch) : null, Failure: null);
        }
        catch (Exception ex) when (Failures.IsRecoverable(ex))
        {
            return new TargetCheck(TooCloseTo: null, Failures.DescribeFailure(ex));
        }
    }

    /// <returns>The hits in target order.</returns>
    private static List<TooCloseObject> ToHits(IObjectsThatCanCauseRemovals otherModObjects, IReadOnlyList<TargetCheck> checks)
    {
        var hits = new List<TooCloseObject>();
        for (var target = 0; target < checks.Count; target++)
        {
            if (checks[target].TooCloseTo is { } hit) hits.Add(new TooCloseObject(target, otherModObjects.Get(hit.Other), hit.Cause));
        }
        return hits;
    }

    private static ImmutableArray<PartFailure> ToFailures(IReadOnlyList<TargetObject> targets, IReadOnlyList<TargetCheck> checks) =>
    [
        .. Enumerable.Range(0, checks.Count)
            .Where(index => checks[index].Failure != null)
            .Select(index => new PartFailure(
                TooCloseParts.TooCloseCheck, checks[index].Failure, new FailureSubject(targets[index].Key.ToString(), targets[index].EditorId, targets[index].Id.Index))),
    ];

    private static ZoneHit? FindFirstTooCloseOther(
        TargetObject target, IObjectsThatCanCauseRemovals otherModObjects, IBaseObjectShapes shapes, ImmutableArray<ZoneGrowth> zones, NpcTooCloseRule npcRule, Scratch scratch) =>
        npcRule.ThenFirstStuckNpc(
            ZoneGrowth.FirstHit(zones, zone => FindFirstCentreInBoxZone(target, otherModObjects, shapes, zone, scratch.Query)),
            target,
            scratch.Npcs);

    /// <summary>The lowest other-mod object that can cause removals whose bounds centre lies in the target's BoundingBox zone, or null.</summary>
    public static OtherId? FindFirstCentreInBoxZone(
        TargetObject target,
        IObjectsThatCanCauseRemovals otherModObjects,
        IBaseObjectShapes shapes,
        ZoneGrowth growth,
        ObjectQueryScratch scratch)
    {
        var expanded = growth.GrownScaledBox(shapes.Of(target.Base).Box, target.Transform.Scale);
        var position = target.Transform.Position;
        var rotation = target.Transform.Rotation;
        return otherModObjects.FirstWithCentreInside(
            target.SpaceKey,
            Boxes.WorldAabb(expanded, position, rotation),
            centre => Boxes.IsInsideOrientedBox(centre, position, rotation, expanded),
            scratch);
    }
}
