using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>What became of a moved replacement, from the final decisions.</summary>
internal enum ReplacementOutcome { KeptWhereMoved, KeptShrunkOrTurned, Removed }

/// <summary>A fitted object's change against the placement it stood at, as the log and the report file show it.</summary>
/// <param name="SizeShare">The new scale as a share of the scale before; 1 when it was not shrunk.</param>
/// <param name="TurnDegrees">How far the heading turned, positive one way and negative the other.</param>
internal readonly record struct FitChange(float SizeShare, float TurnDegrees);

/// <summary>The outcomes of the moved replacements and the fitted objects, read from the final decisions.</summary>
internal static class FitOutcomes
{
    public static ReplacementOutcome OutcomeOf(IRemovalDecisions final, TargetId target) => final.Of(target) switch
    {
        Decision.Removed => ReplacementOutcome.Removed,
        Decision.Fitted => ReplacementOutcome.KeptShrunkOrTurned,
        _ => ReplacementOutcome.KeptWhereMoved,
    };

    /// <summary>The moved replacements, in target order.</summary>
    public static IEnumerable<TargetObject> MovedReplacements(CollectedObjects world) =>
        world.Targets.Where(target => target.IsMovedReplacement);

    /// <summary>The target plugin's own objects kept shrunk or turned where they stand, in target order.</summary>
    public static IEnumerable<TargetObject> OwnObjectsShrunkOrTurned(CollectedObjects world, IRemovalDecisions final) =>
        world.Targets.Where(target => !target.IsMovedReplacement && final.Of(target.Id) is Decision.Fitted);

    /// <summary>Null unless the object was shrunk or turned.</summary>
    public static FitChange? ChangeOf(TargetObject target, IRemovalDecisions final) =>
        final.Of(target.Id) is Decision.Fitted fitted
            ? new FitChange(fitted.To.Scale / target.Placement.Scale, float.RadiansToDegrees(fitted.To.Rotation.Z - target.Placement.Rotation.Z))
            : null;
}
