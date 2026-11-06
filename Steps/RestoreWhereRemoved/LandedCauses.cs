using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>The causes of a too-close check with the objects the follow-up pulls down added where they land.</summary>
internal static class LandedCauses
{
    /// <param name="landed">The objects where they land, in id order.</param>
    /// <returns>
    /// The world with <paramref name="landed"/> added after its causes, numbered on from them, so an id at or past the first landed
    /// one names a landed object; the world itself when none landed.
    /// </returns>
    public static CollectedObjects AddedAfter(CollectedObjects world, IReadOnlyList<OtherObject> landed)
    {
        if (landed.Count == 0) return world;
        var firstLanded = world.OtherModObjects.Length;
        return world with { OtherModObjects = [.. world.OtherModObjects, .. landed.Select((other, index) => other with { Id = new OtherId(firstLanded + index) })] };
    }
}
