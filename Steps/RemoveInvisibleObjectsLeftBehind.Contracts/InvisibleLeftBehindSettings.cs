using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <param name="Surroundings">The surroundings rule; its look-around distance reaches from an invisible target object to the target's visible objects.</param>
public sealed record InvisibleLeftBehindSettings(LeftBehindSurroundings Surroundings);
