using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>How many invisible other-mod objects share one way of being invisible.</summary>
public readonly record struct InvisibleObjectCount(ObjectVisibility Visibility, int Count);

/// <summary>The other-mod objects the too-close step leaves out as a whole.</summary>
/// <param name="Invisible">Invisible other-mod objects of the spaces holding a visible target, by what makes them invisible, in the order each first appears among the other-mod objects.</param>
/// <param name="PlacedNpcs">Placed NPCs among the other-mod objects of the target spaces.</param>
public sealed record OtherObjectsLeftOut(ImmutableArray<InvisibleObjectCount> Invisible, int PlacedNpcs);
