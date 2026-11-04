using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
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

    /// <summary>The placements to try an object at, and the group whose rules judge them.</summary>
    private readonly record struct Trial(int Target, FitGroup Group, ImmutableArray<EulerPlacement> Placements);

    /// <param name="Groups">The objects of each group are tried with its own settings and rules; an object is in one group.</param>
    /// <param name="Resting">Names the objects others rest on, which are left to the removal rounds.</param>
    /// <param name="Decisions">The run's decisions before any round, which the fits are added to.</param>
    public sealed record Input(
        ImmutableArray<TargetObject> Targets,
        IObjectsToKeep ObjectsToKeep,
        IEnumerable<FitGroup> Groups,
        IRestingRule Resting,
        IBaseObjectShapes Shapes,
        WorkOrder Order,
        Execution Execution,
        IRunDecisions Decisions);

    public static Decided<FitResult> Fit(Input input)
    {
        var result = FindFits(input);
        return new Decided<FitResult>(result, input.Decisions.ApplyFits(result.Fits));
    }

    private static FitResult FindFits(Input input)
    {
        var (targets, objectsToKeep, shapes, order, execution) = (input.Targets, input.ObjectsToKeep, input.Shapes, input.Order, input.Execution);
        var fitGroups = input.Groups.ToList();
        KeyedGroups.RequireEachItemInOneGroup(fitGroups.Select(group => group.Judged.Objects.AsEnumerable()));
        var trialOf = fitGroups
            .SelectMany(group => TrialsOf(group, targets, objectsToKeep, shapes))
            .ToDictionary(trial => trial.Target);
        List<int> candidates = [.. trialOf.Keys.Order()];
        var clashes = CheckEach(targets, candidates, target => ClashWhereItStands(target, trialOf[target.Id.Index].Group), order, execution);
        var clashOf = candidates.Zip(clashes).Where(pair => pair.Second is not null).ToDictionary(pair => pair.First, pair => pair.Second!);
        List<int> clashing = [.. clashOf.Keys.Order()];
        var (restedOn, restingFailure) = FindRestedOn(clashing, input.Resting);
        List<int> tried = restingFailure is null ? [.. clashing.Where(index => !restedOn.Contains(index))] : [];
        var fits = CheckEach(
            targets,
            tried,
            target => FitShrunkOrTurned(target, clashOf[target.Id.Index], trialOf[target.Id.Index]),
            order,
            execution);
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

    /// <summary>One object at a time per worker: an object's checks differ too much in cost to hand out several at once.</summary>
    /// <returns>One result per index, in the given order.</returns>
    private static T[] CheckEach<T>(
        ImmutableArray<TargetObject> targets, IReadOnlyList<int> indexes, Func<TargetObject, T> check, WorkOrder order, Execution execution) =>
        ParallelMap.RunOver(execution, order, indexes, index => check(targets[index]), ParallelMap.OneItemPerRange);

    private static IEnumerable<Trial> TrialsOf(FitGroup group, ImmutableArray<TargetObject> targets, IObjectsToKeep objectsToKeep, IBaseObjectShapes shapes)
    {
        var tries = new PlacementTries(group.Judged.Settings);
        return group.Judged.Objects
            .Select(id => targets[id.Index])
            .Where(target => MayBeAdjusted(target, objectsToKeep))
            .Select(target => new Trial(target.Id.Index, group, tries.For(target, adjustable: shapes.VisibilityOf(target).IsVisible)))
            .Where(trial => !trial.Placements.IsEmpty);
    }

    private static bool MayBeAdjusted(TargetObject target, IObjectsToKeep objectsToKeep) =>
        !objectsToKeep.IsProtected(target.Id.Index) && !objectsToKeep.Groups.IsLinked(target.Id.Index);

    /// <returns>
    /// The fittable rule's clash; null when the object fits where it stands, clashes there by the unfittable rule, or a check
    /// failed there: the removal rounds then judge it as it stands.
    /// </returns>
    private static RemovalReason? ClashWhereItStands(TargetObject target, FitGroup group)
    {
        if (Stops(group.Unfittable.CheckWhereItStands(target))) return null;
        return group.Fittable.CheckWhereItStands(target) is { Failure: null, Clash: { } clash } ? clash : null;
    }

    /// <returns>An empty set and the failure when the search stopped unexpectedly: no object is tried then.</returns>
    private static (IReadOnlySet<int> RestedOn, PartFailure? Failure) FindRestedOn(List<int> clashing, IRestingRule resting) =>
        Failures.Attempt(
            () => ((IReadOnlySet<int>)resting.FindObjectsOthersRestOn([.. clashing.Select(index => new TargetId(index))]).Select(target => target.Index).ToHashSet(), (PartFailure?)null),
            failure => (new HashSet<int>(), PartFailure.OfWholePart(FitParts.RestingCheck, failure)));

    private static ObjectFit FitShrunkOrTurned(TargetObject target, RemovalReason clash, Trial trial)
    {
        using var fittable = trial.Group.Fittable.StartTries(target);
        using var unfittable = trial.Group.Unfittable.StartTries(target);
        string? firstFailure = null;
        foreach (var placement in trial.Placements)
        {
            var check = FirstClash(placement, fittable, unfittable);
            if (check.Failure is not null) firstFailure ??= check.Failure;
            else if (check.Clash is null) return new ObjectFit(new ProposedFit(target.Id, clash, placement), firstFailure);
        }
        return new ObjectFit(Fit: null, firstFailure);
    }

    /// <summary>The fittable rule's check, then the unfittable rule's; the first that clashes or fails counts.</summary>
    /// <remarks>
    /// A try fits only when both pass, so the order decides only which failure a try records. The fittable check comes first
    /// because most tries clash by it, and the unfittable check is then not needed.
    /// </remarks>
    private static PlacementCheck FirstClash(EulerPlacement placement, IPlacementTries fittable, IPlacementTries unfittable)
    {
        var check = fittable.Check(placement);
        return Stops(check) ? check : unfittable.Check(placement);
    }

    private static bool Stops(PlacementCheck check) => check.Clash is not null || check.Failure is not null;

    private static PartFailure FailureOf(TargetObject target, string failure) =>
        new(FitParts.FitCheck, failure, new FailureSubject(target.Key.ToString(), target.EditorId, target.Id.Index));
}
