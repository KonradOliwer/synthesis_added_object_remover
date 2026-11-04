using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <param name="Surroundings">The surroundings rule; its look-around distance reaches from a visible target object to the target's other visible objects.</param>
/// <param name="MinObjectsAround">The fewest of the target's other visible objects within the look-around distance, standing or not, for an object to be removed.</param>
public sealed record VisibleLeftBehindSettings(LeftBehindSurroundings Surroundings, int MinObjectsAround);

/// <param name="Surroundings">The surroundings rule; its look-around distance reaches from an invisible target object to the target's visible objects.</param>
public sealed record InvisibleLeftBehindSettings(LeftBehindSurroundings Surroundings);

/// <summary>The visible kinds the user ticked as always kept; they stand whether or not the left-behind visible objects step runs.</summary>
public sealed record NeverRemovedVisibleKinds(IReadOnlySet<VisibleObjectKind> Kinds);

/// <summary>The invisible kinds the user ticked as always kept; they stand whether or not the left-behind invisible objects step runs.</summary>
public sealed record NeverRemovedInvisibleKinds(IReadOnlySet<InvisibleObjectKind> Kinds);
