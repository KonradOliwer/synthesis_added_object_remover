namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>What one version of a placed record says about whether the game has it, read by the collector's presence rule.</summary>
/// <param name="Removed">The version is deleted or disabled the way a removal disables a record; its enable parent does not matter then.</param>
/// <param name="Parent">The record the version is enabled with; null when it has none.</param>
/// <param name="OppositeOfParent">The version is enabled exactly when its parent is not.</param>
public readonly record struct PlacedPresence(bool Removed, RecordKey? Parent, bool OppositeOfParent);
