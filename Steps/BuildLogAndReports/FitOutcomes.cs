using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// What became of a changed object or of an object kept shrunk or turned, from the final decisions; a changed object kept where the
/// cleaned mod put it may then go back where the cleaned mod was removed.
/// </summary>
internal enum ChangedObjectOutcome { KeptWherePut, KeptThenRestoredWhereRemoved, KeptShrunkOrTurned, Restored, Removed }

/// <summary>A fitted object's change against the placement it stood at, as the log and the report file show it.</summary>
/// <param name="SizeShare">The new scale as a share of the scale before; 1 when it was not shrunk.</param>
/// <param name="TurnDegrees">How far the heading turned, positive one way and negative the other.</param>
internal readonly record struct FitChange(float SizeShare, float TurnDegrees);

/// <summary>The outcomes of the changed objects and the fitted objects, read from the final decisions.</summary>
internal static class FitOutcomes
{
    public static ChangedObjectOutcome OutcomeOf(IRemovalDecisions final, TargetId target) => RoundEffects.Of(final.Of(target)).Outcome switch
    {
        RoundOutcome.Removed => ChangedObjectOutcome.Removed,
        RoundOutcome.Restored => ChangedObjectOutcome.Restored,
        RoundOutcome.KeptShrunkOrTurned => ChangedObjectOutcome.KeptShrunkOrTurned,
        RoundOutcome.Untouched or RoundOutcome.KeptAsReferenced => ChangedObjectOutcome.KeptWherePut,
        var unknown => throw new UnreachableException($"Unknown round outcome {unknown}."),
    };

    /// <param name="restoredWhereRemoved">The earlier version of each target that went back where the cleaned mod was removed.</param>
    public static ChangedObjectOutcome ChangedObjectOutcomeOf(IRemovalDecisions final, IReadOnlyDictionary<TargetId, EarlierVersion> restoredWhereRemoved, TargetId target)
    {
        var outcome = OutcomeOf(final, target);
        return outcome == ChangedObjectOutcome.KeptWherePut && restoredWhereRemoved.ContainsKey(target) ? ChangedObjectOutcome.KeptThenRestoredWhereRemoved : outcome;
    }

    /// <summary>The earlier version of each target the step that restores where the cleaned mod was removed put back.</summary>
    public static IReadOnlyDictionary<TargetId, EarlierVersion> RestoredWhereRemoved(RestoreWhereRemovedResult result) =>
        result.Objects.Where(judged => judged.IsRestored && judged.Change.Target is not null).ToDictionary(judged => judged.Change.Target!.Value, judged => judged.Change.Earlier);

    /// <summary>The other mods' objects the cleaned mod changed, of every origin but the own one, in target order.</summary>
    public static IEnumerable<TargetObject> ChangedObjects(CollectedObjects world) =>
        world.Targets.Where(target => target.Origin != TargetOrigin.CleanedMod);

    /// <summary>The target plugin's own objects kept shrunk or turned where they stand, in target order.</summary>
    public static IEnumerable<TargetObject> OwnObjectsShrunkOrTurned(CollectedObjects world, IRemovalDecisions final) =>
        TargetOrigins.Of(world.Targets, TargetOrigin.CleanedMod).Where(target => OutcomeOf(final, target.Id) == ChangedObjectOutcome.KeptShrunkOrTurned);

    /// <summary>Null unless the object was shrunk or turned.</summary>
    public static FitChange? ChangeOf(TargetObject target, IRemovalDecisions final) =>
        final.Of(target.Id) is Decision.Fitted fitted
            ? new FitChange(fitted.To.Scale / target.Placement.Scale, float.RadiansToDegrees(fitted.To.Rotation.Z - target.Placement.Rotation.Z))
            : null;

    /// <summary>
    /// The placement the object was given: where it was shrunk or turned to, or where a restore put it (each of position, rotation
    /// and scale from the earlier version only when it goes back); null when it kept its placement.
    /// </summary>
    /// <param name="restoredWhereRemoved">The earlier version of each target that went back where the cleaned mod was removed.</param>
    public static EulerPlacement? NewPlacementOf(IRemovalDecisions final, IReadOnlyDictionary<TargetId, EarlierVersion> restoredWhereRemoved, TargetObject target) =>
        final.Of(target.Id) is Decision.Fitted fitted ? fitted.To : EarlierVersionWritten(final, restoredWhereRemoved, target.Id) is { } earlier ? target.RestoredAs(earlier).Placement : null;

    /// <summary>The earlier version the patch writes back for the target, by either restore; null when it writes none.</summary>
    /// <param name="restoredWhereRemoved">The earlier version of each target that went back where the cleaned mod was removed.</param>
    public static EarlierVersion? EarlierVersionWritten(IRemovalDecisions final, IReadOnlyDictionary<TargetId, EarlierVersion> restoredWhereRemoved, TargetId target) =>
        (final.Of(target) as Decision.Restored)?.EarlierVersion ?? restoredWhereRemoved.GetValueOrDefault(target);
}
