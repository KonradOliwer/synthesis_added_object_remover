using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>What the also-remove rounds read.</summary>
/// <param name="ObjectsOfAnyPlugin">Only for OnlyWhatLosesItsSupport: every visible object of any plugin that can support a target.</param>
internal sealed record RoundsInput(SupportReads Reads, IVisibleObjectsOfAnyPlugin? ObjectsOfAnyPlugin)
{
    /// <param name="caches">Its shapes, triangles, surface samples and terrain are read.</param>
    public static RoundsInput From(ImmutableArray<TargetObject> targets, IVisibleObjectsOfAnyPlugin? objectsOfAnyPlugin, ILoadOrderCaches caches, Execution execution) =>
        new(SupportReads.From(targets, caches, execution), objectsOfAnyPlugin);
}
