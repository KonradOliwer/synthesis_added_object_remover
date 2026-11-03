namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <param name="Surroundings">The surroundings rule; its look-around distance reaches from a visible target object to the target's other visible objects.</param>
/// <param name="MinObjectsAround">The fewest of the target's other visible objects within the look-around distance, standing or not, for an object to be removed.</param>
public sealed record VisibleLeftBehindSettings(LeftBehindSurroundings Surroundings, int MinObjectsAround);
