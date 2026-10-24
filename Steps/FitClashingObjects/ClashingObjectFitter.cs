using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.FitClashingObjects;

/// <summary>
/// Runs before the removal rounds. Each object that may be tried and clashes where it stands, by the removal steps' own rules, is
/// tried at the placements of <see cref="PlacementTries"/> in order and proposed at the first one that every rule accepts. An
/// object that fits nowhere is left to the removal rounds. Objects that are always kept stay as they are, and so do linked
/// objects, since a linked group is removed or kept whole. Only a visible object is shrunk or turned: an invisible one has no
/// shape to fit. Each object is tried on its own, so the result does not depend on the thread count.
/// </summary>
public static class ClashingObjectFitter
{
    /// <summary>What trying one object found.</summary>
    /// <param name="Fit">Null when it fits nowhere.</param>
    /// <param name="Failure">The first failed check among its tries; null when none failed.</param>
    private readonly record struct ObjectFit(ProposedFit? Fit, string? Failure);

    /// <param name="rules">The removal steps' rules, in the order of their rounds.</param>
    public static FitResult Fit(
        ImmutableArray<TargetObject> targets,
        IObjectsToKeep objectsToKeep,
        IReadOnlyList<IPlacementRule> rules,
        IBaseObjectShapes shapes,
        WorkOrder order,
        Execution execution,
        FitSettings settings)
    {
        var tries = new PlacementTries(settings);
        var triesByTarget = targets
            .Where(target => MayBeMoved(target, objectsToKeep))
            .Select(target => (Target: target.Id.Index, Tries: tries.For(target, adjustable: shapes.VisibilityOf(target).IsVisible)))
            .Where(candidate => !candidate.Tries.IsEmpty)
            .ToDictionary(candidate => candidate.Target, candidate => candidate.Tries);
        List<int> candidates = [.. triesByTarget.Keys.Order()];
        var fits = ParallelMap.RunOver(
            execution,
            order,
            candidates,
            static () => (object?)null,
            (index, _) => FitWhereItClashes(targets[index], triesByTarget[index], rules),
            ParallelMap.OneItemPerRange);
        return new FitResult(
            [.. fits.Select(tried => tried?.Fit).OfType<ProposedFit>()],
            fits.Count(tried => tried is not null),
            [.. candidates.Zip(fits).Where(pair => pair.Second?.Failure is not null).Select(pair => FailureOf(targets[pair.First], pair.Second!.Value.Failure!))]);
    }

    private static bool MayBeMoved(TargetObject target, IObjectsToKeep objectsToKeep) =>
        !objectsToKeep.IsProtected(target.Id.Index) && !objectsToKeep.Groups.IsLinked(target.Id.Index);

    /// <returns>Null when the object fits where it stands, or its check there failed: the removal rounds then judge it as it stands.</returns>
    private static ObjectFit? FitWhereItClashes(TargetObject target, ImmutableArray<PlacementTry> tries, IReadOnlyList<IPlacementRule> rules)
    {
        if (FirstClash(target.Id, target.Placement, rules).Clash is not { } clash) return null;

        string? firstFailure = null;
        foreach (var placementTry in tries)
        {
            var check = FirstClash(target.Id, placementTry.Placement, rules);
            if (check.Failure is not null) firstFailure ??= check.Failure;
            else if (check.Clash is null) return new ObjectFit(new ProposedFit(target.Id, clash, placementTry.Kind, placementTry.Placement), firstFailure);
        }
        return new ObjectFit(Fit: null, firstFailure);
    }

    /// <summary>The first rule that would remove the object at the placement, or whose check failed there; none when every rule accepts it.</summary>
    private static PlacementCheck FirstClash(TargetId target, EulerPlacement placement, IReadOnlyList<IPlacementRule> rules)
    {
        foreach (var rule in rules)
        {
            var check = rule.Check(target, placement);
            if (check.Clash is not null || check.Failure is not null) return check;
        }
        return default;
    }

    private static PartFailure FailureOf(TargetObject target, string failure) =>
        new(FitParts.FitCheck, failure, new FailureSubject(target.Key.ToString(), target.EditorId, target.Id.Index));
}
