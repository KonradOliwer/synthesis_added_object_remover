using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>What the removal steps' final decisions made of a changed object or of an object kept shrunk or turned.</summary>
internal enum ChangedObjectOutcome { KeptWherePut, KeptShrunkOrTurned, Restored, Removed }

/// <summary>What the step that restores where the cleaned mod was removed then did to a target the removal steps kept.</summary>
internal enum WhereRemovedThen { RestoredWhereRemoved, RestoredByFollowUp, RemovedByFollowUp }

/// <summary>What the step that restores where the cleaned mod was removed did to a target.</summary>
/// <param name="Earlier">The earlier version the patch writes back; null for a target the follow-up removes.</param>
internal readonly record struct WhereRemovedOutcome(WhereRemovedThen Then, EarlierVersion? Earlier);

/// <summary>What became of a target in the end: what the removal steps decided, then what the restore did to it, if anything.</summary>
internal readonly record struct ChangedObjectFate(ChangedObjectOutcome Run, WhereRemovedThen? Then);

/// <summary>A fitted object's change against the placement it stood at, as the log and the report file show it.</summary>
/// <param name="SizeShare">The new scale as a share of the scale before; 1 when it was not shrunk.</param>
/// <param name="TurnDegrees">How far the heading turned, positive one way and negative the other.</param>
internal readonly record struct FitChange(float SizeShare, float TurnDegrees);

/// <summary>The outcomes of the changed objects and the fitted objects, read from the final decisions and the restore's result.</summary>
internal static class FitOutcomes
{
    public static ChangedObjectOutcome OutcomeOf(IRemovalDecisions final, TargetId target) => (final.Of(target)?.Effect ?? RoundEffect.Untouched).Outcome switch
    {
        RoundOutcome.Removed => ChangedObjectOutcome.Removed,
        RoundOutcome.Restored => ChangedObjectOutcome.Restored,
        RoundOutcome.KeptShrunkOrTurned => ChangedObjectOutcome.KeptShrunkOrTurned,
        RoundOutcome.Untouched or RoundOutcome.KeptAsReferenced => ChangedObjectOutcome.KeptWherePut,
        var unknown => throw new UnreachableException($"Unknown round outcome {unknown}."),
    };

    /// <remarks>
    /// The restore judges by area only targets the removal steps left undecided, and its follow-up takes along only those and the
    /// ones kept shrunk or turned, which a round may still remove; so it never acts on a target the removal steps restored or removed.
    /// </remarks>
    /// <param name="whereRemoved">What the step that restores where the cleaned mod was removed did to each target it acted on.</param>
    public static ChangedObjectFate FateOf(IRemovalDecisions final, IReadOnlyDictionary<TargetId, WhereRemovedOutcome> whereRemoved, TargetId target)
    {
        var run = OutcomeOf(final, target);
        if (!whereRemoved.TryGetValue(target, out var after)) return new ChangedObjectFate(run, Then: null);
        return (run, after.Then) switch
        {
            (ChangedObjectOutcome.KeptWherePut, _) or (ChangedObjectOutcome.KeptShrunkOrTurned, not WhereRemovedThen.RestoredWhereRemoved) => new ChangedObjectFate(run, after.Then),
            _ => throw new UnreachableException($"The restore's outcome {after.Then} names target {target}, which the removal steps decided as {run}."),
        };
    }

    /// <summary>
    /// What the step that restores where the cleaned mod was removed did to each target it acted on: put back where its area is
    /// cleared, or restored or removed by the follow-up of what went back.
    /// </summary>
    /// <remarks>The follow-up never decides a target that goes back by its area, so a target listed twice is a bug.</remarks>
    public static IReadOnlyDictionary<TargetId, WhereRemovedOutcome> WhereRemovedOutcomes(RestoreWhereRemovedResult result)
    {
        var outcomes = new Dictionary<TargetId, WhereRemovedOutcome>();
        var byArea = result.Objects.Where(judged => judged.IsRestored && judged.Change.Target is not null)
            .Select(judged => (Target: judged.Change.Target!.Value, Outcome: new WhereRemovedOutcome(WhereRemovedThen.RestoredWhereRemoved, judged.Change.Earlier)));
        foreach (var (target, outcome) in byArea.Concat(result.FollowUp.Targets.Select(followed => (followed.Target, FollowUpOutcomeOf(followed)))))
        {
            if (!outcomes.TryAdd(target, outcome)) throw new UnreachableException($"The restore decided target {target} twice: {outcomes[target].Then} and {outcome.Then}.");
        }
        return outcomes;
    }

    /// <summary>What happened to a target the removal steps kept, as the log and the report file say it after "then".</summary>
    public static string ThenPhrase(WhereRemovedThen then) => then switch
    {
        WhereRemovedThen.RestoredWhereRemoved => "restored where the cleaned mod was removed",
        WhereRemovedThen.RestoredByFollowUp => "restored by the follow-up of what went back",
        WhereRemovedThen.RemovedByFollowUp => "removed by the follow-up of what went back",
        _ => throw new UnreachableException($"Unknown outcome of the restore {then}."),
    };

    /// <summary>", then removed by the follow-up of what went back"; nothing when the restore did nothing to it.</summary>
    public static string ThenSuffix(ChangedObjectFate fate) => fate.Then is { } then ? $", then {ThenPhrase(then)}" : string.Empty;

    private static WhereRemovedOutcome FollowUpOutcomeOf(FollowUpTarget followed) =>
        new(followed.RestoredTo is null ? WhereRemovedThen.RemovedByFollowUp : WhereRemovedThen.RestoredByFollowUp, followed.RestoredTo);

    /// <summary>The other mods' objects the cleaned mod changed, of every origin but the own one, in target order.</summary>
    public static IEnumerable<TargetObject> ChangedObjects(CollectedObjects world) =>
        world.Targets.Where(target => target.Origin != TargetOrigin.CleanedMod);

    /// <summary>The target plugin's own objects kept shrunk or turned where they stand, and not taken along by the restore's follow-up, in target order.</summary>
    /// <param name="whereRemoved">What the step that restores where the cleaned mod was removed did to each target it acted on.</param>
    public static IEnumerable<TargetObject> OwnObjectsShrunkOrTurned(CollectedObjects world, IRemovalDecisions final, IReadOnlyDictionary<TargetId, WhereRemovedOutcome> whereRemoved) =>
        TargetOrigins.Of(world.Targets, TargetOrigin.CleanedMod)
            .Where(target => OutcomeOf(final, target.Id) == ChangedObjectOutcome.KeptShrunkOrTurned && !whereRemoved.ContainsKey(target.Id));

    /// <summary>Null unless the object was shrunk or turned.</summary>
    public static FitChange? ChangeOf(TargetObject target, IRemovalDecisions final) =>
        final.Of(target.Id) is Decision.Fitted fitted
            ? new FitChange(fitted.To.Scale / target.Placement.Scale, float.RadiansToDegrees(fitted.To.Rotation.Z - target.Placement.Rotation.Z))
            : null;

    /// <summary>
    /// The placement the object was given in the end: where a restore put it (each of position, rotation and scale from the earlier
    /// version only when it goes back), or where it was shrunk or turned to; null when it kept its placement or was removed.
    /// </summary>
    /// <param name="whereRemoved">What the step that restores where the cleaned mod was removed did to each target it acted on.</param>
    public static EulerPlacement? NewPlacementOf(IRemovalDecisions final, IReadOnlyDictionary<TargetId, WhereRemovedOutcome> whereRemoved, TargetObject target)
    {
        if (whereRemoved.TryGetValue(target.Id, out var after)) return after.Earlier is { } earlier ? target.RestoredAs(earlier).Placement : null;
        return final.Of(target.Id) switch
        {
            Decision.Fitted fitted => fitted.To,
            Decision.Restored => PlacementItStandsAt(final, target),
            _ => null,
        };
    }

    /// <summary>Where the decisions leave the target: the placement a fit or a restore gave it, otherwise the one it has.</summary>
    public static EulerPlacement PlacementItStandsAt(IRemovalDecisions final, TargetObject target) => final.TargetsAsTheyStand([target])[0].Placement;

    /// <summary>The earlier version the patch writes back for the target, by either restore; null when it writes none.</summary>
    /// <param name="whereRemoved">What the step that restores where the cleaned mod was removed did to each target it acted on.</param>
    public static EarlierVersion? EarlierVersionWritten(IRemovalDecisions final, IReadOnlyDictionary<TargetId, WhereRemovedOutcome> whereRemoved, TargetId target) =>
        whereRemoved.TryGetValue(target, out var after) ? after.Earlier : (final.Of(target) as Decision.Restored)?.EarlierVersion;
}
