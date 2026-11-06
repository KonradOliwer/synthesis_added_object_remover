using System.Diagnostics;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>Which supporters left their place; the terrain only where the ground goes down.</summary>
internal static class SupporterGone
{
    /// <param name="isTerrainGone">Whether the ground under the object goes down.</param>
    public static bool IsGone(Supporter supporter, Func<TargetId, bool> isTargetGone, Func<OtherId, bool> isOtherGone, bool isTerrainGone) => supporter.Type switch
    {
        SupporterType.Target => isTargetGone(new TargetId(supporter.Index)),
        SupporterType.PlacedObject => isOtherGone(new OtherId(supporter.Index)),
        SupporterType.Terrain => isTerrainGone,
        _ => throw new UnreachableException($"Unknown supporter type {supporter.Type}."),
    };

    /// <summary>The supporters of <paramref name="contacts"/> that left their place, largest weight first, then in supporter order.</summary>
    /// <param name="isTerrainGone">Whether the ground under the object goes down.</param>
    public static List<SupporterWeight> ThatLeft(CandidateContacts contacts, Func<TargetId, bool> isTargetGone, Func<OtherId, bool> isOtherGone, bool isTerrainGone) =>
        [.. contacts.Supporters.Where(entry => IsGone(entry.Supporter, isTargetGone, isOtherGone, isTerrainGone)).OrderByDescending(entry => entry.Weight)];
}
