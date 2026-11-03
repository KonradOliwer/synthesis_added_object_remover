namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>The visible kinds the user ticked as always kept; they stand whether or not the left-behind visible objects step runs.</summary>
public sealed record NeverRemovedVisibleKinds(IReadOnlySet<VisibleObjectKind> Kinds);
