namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>An error nobody planned for while one target object was checked; the step carried on with the others.</summary>
/// <param name="TargetIndex">The target object's position in the collected target list.</param>
/// <param name="Failure">The exception's type and message.</param>
public sealed record TargetFailure(int TargetIndex, string Failure);
