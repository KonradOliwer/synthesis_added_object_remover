using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.FitClashingObjects;

/// <summary>
/// Runs before the removal rounds. Each object that may be tried and clashes where it stands by the fittable rule only is tried
/// shrunk or turned as <see cref="PlacementTries"/> lists, and proposed at the first placement that both rules accept. An object
/// that clashes by the unfittable rule where it stands, that other objects rest on, or that fits nowhere is left to the removal
/// rounds, which remove it as it stands; what rests on it is then the also-remove step's to judge. Objects that are always kept
/// stay as they are, and so do linked objects, since a linked group is removed or kept whole. Only a visible object is shrunk or
/// turned: an invisible one has no shape to fit. Each object is checked on its own and the objects others rest on are found for
/// all of them at once, so the result does not depend on the thread count.
/// </summary>
public static class ClashingObjectFitter
{
    /// <summary>What trying one object found.</summary>
    /// <param name="Fit">Null when it fits nowhere.</param>
    /// <param name="Failure">The first failed check among its tries; null when none failed.</param>
    private readonly record struct ObjectFit(ProposedFit? Fit, string? Failure);

    public static FitResult Fit(
        ImmutableArray<TargetObject> targets,
        IObjectsToKeep objectsToKeep,
        FitRules rules,
        IBaseObjectShapes shapes,
        WorkOrder order,
        Execution execution,
        FitSettings settings)
    {
        var tries = new PlacementTries(settings);
        var triesByTarget = targets
            .Where(target => MayBeAdjusted(target, objectsToKeep))
            .Select(target => (Target: target.Id.Index, Tries: tries.For(target, adjustable: shapes.VisibilityOf(target).IsVisible)))
            .Where(candidate => !candidate.Tries.IsEmpty)
            .ToDictionary(candidate => candidate.Target, candidate => candidate.Tries);
        List<int> candidates = [.. triesByTarget.Keys.Order()];
        var clashes = CheckEach(targets, candidates, target => ClashWhereItStands(target, rules), order, execution);
        var clashOf = candidates.Zip(clashes).Where(pair => pair.Second is not null).ToDictionary(pair => pair.First, pair => pair.Second!);
        List<int> clashing = [.. clashOf.Keys.Order()];
        var (restedOn, restingFailure) = FindRestedOn(clashing, rules.Resting);
        List<int> tried = restingFailure is null ? [.. clashing.Where(index => !restedOn.Contains(index))] : [];
        var fits = CheckEach(
            targets, tried, target => FitShrunkOrTurned(target, clashOf[target.Id.Index], triesByTarget[target.Id.Index], rules), order, execution);
        return new FitResult(
            [.. fits.Select(fit => fit.Fit).OfType<ProposedFit>()],
            clashing.Count,
            restedOn.Count,
            tried.Count,
            [
                .. restingFailure is { } stopped ? new[] { stopped } : Array.Empty<PartFailure>(),
                .. tried.Zip(fits).Where(pair => pair.Second.Failure is not null).Select(pair => FailureOf(targets[pair.First], pair.Second.Failure!)),
            ]);
    }

    /// <summary>The targets where they stand: each object kept shrunk or turned at its new placement.</summary>
    public static ImmutableArray<TargetObject> PlaceAsTheyStand(ImmutableArray<TargetObject> targets, IRemovalDecisions decisions) =>
        [.. targets.Select(target => decisions.Of(target.Id) is Decision.Fitted fitted ? target.PlacedAt(fitted.To) : target)];

    /// <summary>
    /// The visible targets where they stand: a new index with each object kept shrunk or turned at its new placement, or the
    /// collected index when there is none. An object kept shrunk or turned and removed later stands where it stood, as the patch
    /// leaves it.
    /// </summary>
    public static IVisibleTargetObjects IndexAsTheyStand(ImmutableArray<TargetObject> targets, IRemovalDecisions decisions, IObjectCaches caches) =>
        decisions.All().Any(entry => entry.Decision is Decision.Fitted)
            ? caches.IndexVisibleTargets(PlaceAsTheyStand(targets, decisions))
            : caches.VisibleTargets;

    /// <summary>One object at a time per worker: an object's checks differ too much in cost to hand out several at once.</summary>
    /// <returns>One result per index, in the given order.</returns>
    private static T[] CheckEach<T>(
        ImmutableArray<TargetObject> targets, IReadOnlyList<int> indexes, Func<TargetObject, T> check, WorkOrder order, Execution execution) =>
        ParallelMap.RunOver(execution, order, indexes, static () => (object?)null, (index, _) => check(targets[index]), ParallelMap.OneItemPerRange);

    private static bool MayBeAdjusted(TargetObject target, IObjectsToKeep objectsToKeep) =>
        !objectsToKeep.IsProtected(target.Id.Index) && !objectsToKeep.Groups.IsLinked(target.Id.Index);

    /// <returns>
    /// The fittable rule's clash; null when the object fits where it stands, clashes there by the unfittable rule, or a check
    /// failed there: the removal rounds then judge it as it stands.
    /// </returns>
    private static RemovalReason? ClashWhereItStands(TargetObject target, FitRules rules)
    {
        if (Stops(rules.Unfittable.Check(target.Id, target.Placement))) return null;
        return rules.Fittable.Check(target.Id, target.Placement) is { Failure: null, Clash: { } clash } ? clash : null;
    }

    /// <returns>An empty set and the failure when the search stopped unexpectedly: no object is tried then.</returns>
    private static (IReadOnlySet<int> RestedOn, PartFailure? Failure) FindRestedOn(List<int> clashing, IRestingRule resting) =>
        Failures.Attempt(
            () => ((IReadOnlySet<int>)resting.FindObjectsOthersRestOn([.. clashing.Select(index => new TargetId(index))]).Select(target => target.Index).ToHashSet(), (PartFailure?)null),
            failure => (new HashSet<int>(), PartFailure.OfWholePart(FitParts.RestingCheck, failure)));

    private static ObjectFit FitShrunkOrTurned(TargetObject target, RemovalReason clash, ImmutableArray<EulerPlacement> tries, FitRules rules)
    {
        string? firstFailure = null;
        foreach (var placement in tries)
        {
            var check = FirstClash(target.Id, placement, rules);
            if (check.Failure is not null) firstFailure ??= check.Failure;
            else if (check.Clash is null) return new ObjectFit(new ProposedFit(target.Id, clash, placement), firstFailure);
        }
        return new ObjectFit(Fit: null, firstFailure);
    }

    /// <summary>The unfittable rule's check, then the fittable rule's, in the order of their removal rounds; the first that clashes or fails counts.</summary>
    private static PlacementCheck FirstClash(TargetId target, EulerPlacement placement, FitRules rules)
    {
        var unfittable = rules.Unfittable.Check(target, placement);
        return Stops(unfittable) ? unfittable : rules.Fittable.Check(target, placement);
    }

    private static bool Stops(PlacementCheck check) => check.Clash is not null || check.Failure is not null;

    private static PartFailure FailureOf(TargetObject target, string failure) =>
        new(FitParts.FitCheck, failure, new FailureSubject(target.Key.ToString(), target.EditorId, target.Id.Index));
}
