using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <summary>The other-mod object that can cause removals which each invisible target object sits inside.</summary>
/// <param name="containing">By target index; null for a target that is visible or sits inside no object.</param>
public sealed class ContainingObjects(ImmutableArray<OtherObject?> containing)
{
    public OtherObject? Of(int targetIndex) => containing[targetIndex];
}
