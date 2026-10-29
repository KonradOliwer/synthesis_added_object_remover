using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>What became of a changed object or of an object kept shrunk or turned, from the final decisions.</summary>
internal enum ChangedObjectOutcome { KeptWherePut, KeptShrunkOrTurned, Restored, Removed }

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

    /// <summary>The placement the object was given: where it was shrunk or turned to, or where it was restored to; null when it kept its placement.</summary>
    public static EulerPlacement? NewPlacementOf(IRemovalDecisions final, TargetId target) => final.Of(target) switch
    {
        Decision.Fitted fitted => fitted.To,
        Decision.Restored restored => restored.EarlierVersion.Placement,
        _ => null,
    };
}
