using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>What became of a moved replacement, from the final decisions.</summary>
internal enum ReplacementOutcome { KeptWhereMoved, KeptShrunkOrTurned, MovedBack, MovedBackShrunkOrTurned, Removed }

/// <summary>A fitted object's change against the placement it was tried from, as the log and the report file show it.</summary>
/// <param name="SizeShare">The new scale as a share of the scale before; 1 when it was not shrunk.</param>
/// <param name="TurnDegrees">How far the heading turned, positive one way and negative the other.</param>
internal readonly record struct FitChange(float SizeShare, float TurnDegrees);

/// <summary>The outcomes of the moved replacements and the fitted objects, read from the final decisions.</summary>
internal static class FitOutcomes
{
    public static ReplacementOutcome OutcomeOf(IRemovalDecisions final, TargetId target) => final.Of(target) switch
    {
        Decision.Removed => ReplacementOutcome.Removed,
        Decision.Fitted { Kind: FitKind.ShrunkOrTurned } => ReplacementOutcome.KeptShrunkOrTurned,
        Decision.Fitted { Kind: FitKind.MovedBack } => ReplacementOutcome.MovedBack,
        Decision.Fitted { Kind: FitKind.MovedBackShrunkOrTurned } => ReplacementOutcome.MovedBackShrunkOrTurned,
        _ => ReplacementOutcome.KeptWhereMoved,
    };

    /// <summary>The moved replacements, in target order.</summary>
    public static IEnumerable<TargetObject> MovedReplacements(CollectedObjects world) =>
        world.Targets.Where(target => target.Replaced is not null);

    /// <summary>The target plugin's own objects kept shrunk or turned where they stand, in target order.</summary>
    public static IEnumerable<TargetObject> OwnObjectsShrunkOrTurned(CollectedObjects world, IRemovalDecisions final) =>
        world.Targets.Where(target => target.Replaced is null && final.Of(target.Id) is Decision.Fitted);

    /// <summary>Null unless the object was shrunk or turned: against where it stood, or, moved back, against the placement it replaces.</summary>
    public static FitChange? ChangeOf(TargetObject target, IRemovalDecisions final)
    {
        if (final.Of(target.Id) is not Decision.Fitted { Kind: not FitKind.MovedBack } fitted) return null;
        var from = fitted.Kind == FitKind.ShrunkOrTurned ? target.Placement : target.Replaced!.Value.Placement;
        return new FitChange(fitted.To.Scale / from.Scale, float.RadiansToDegrees(fitted.To.Rotation.Z - from.Rotation.Z));
    }
}
