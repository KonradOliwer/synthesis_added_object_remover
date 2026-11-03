namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <param name="Surroundings">The surroundings rule; its look-around distance reaches from a visible target object to the target's other visible objects.</param>
/// <param name="NeverRemove">Visible object kinds that are never removed when left behind.</param>
public sealed record VisibleLeftBehindSettings(LeftBehindSurroundings Surroundings, NeverRemovedVisibleKinds NeverRemove);
