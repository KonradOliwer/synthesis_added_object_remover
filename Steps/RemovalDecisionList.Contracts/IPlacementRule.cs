namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>A removal step's rule asked about one target object standing at given placements, its own or shrunk or turned ones.</summary>
public interface IPlacementRule
{
    /// <summary>The checks of one target object, run one after another on one thread; dispose of them after the last. Thread-safe.</summary>
    IPlacementTries StartTries(TargetId target);
}

/// <summary>
/// The checks of one target object. The first check is exactly the step's own check of the object standing at that placement. A
/// later check clashes or fails as the step's own check would, unless a part of that check stops unexpectedly, and its clash may
/// be another one the step finds there.
/// </summary>
public interface IPlacementTries : IDisposable
{
    PlacementCheck Check(EulerPlacement placement);
}

/// <param name="Clash">Why the rule would remove the object there; null when it would not.</param>
/// <param name="Failure">Why the check stopped unexpectedly; null when it did not.</param>
public readonly record struct PlacementCheck(RemovalReason? Clash, string? Failure);
