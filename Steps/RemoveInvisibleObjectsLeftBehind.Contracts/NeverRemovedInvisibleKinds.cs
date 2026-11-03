using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <summary>The invisible kinds the user ticked as always kept; they stand whether or not the left-behind invisible objects step runs.</summary>
public sealed record NeverRemovedInvisibleKinds(IReadOnlySet<InvisibleObjectKind> Kinds);
