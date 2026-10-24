namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>A removal step's rule asked about one target object standing at a placement other than its own.</summary>
public interface IPlacementRule
{
    /// <summary>Exactly the step's own check of the object, with the object standing at <paramref name="placement"/>. Thread-safe.</summary>
    PlacementCheck Check(TargetId target, EulerPlacement placement);
}

/// <param name="Clash">Why the rule would remove the object there; null when it would not.</param>
/// <param name="Failure">Why the check stopped unexpectedly; null when it did not.</param>
public readonly record struct PlacementCheck(RemovalReason? Clash, string? Failure);
