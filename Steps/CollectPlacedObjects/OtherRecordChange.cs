using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// Whether the game shows another mod's placed record differently from its accepted values (<see cref="AcceptedVersions"/>), and
/// which plugins the log names for it. Each object property is decided on its own; the object is changed when any of them is.
/// </summary>
internal static class OtherRecordChange
{
    /// <summary>Ranks a change only the patch holds below every version index, so any real mod's change is named first.</summary>
    private const int PatchOnlyRank = -1;

    /// <summary>The object properties, each as "the two versions differ": position, rotation, scale, base and presence; base and presence are exact.</summary>
    private static readonly ImmutableArray<Func<PlacedVersionFact, PlacedVersionFact, bool>> ObjectProperties =
    [
        static (first, second) => ResavedPlacedParts.PositionsDiffer(first.Placement.Position, second.Placement.Position),
        static (first, second) => ResavedPlacedParts.RotationsDiffer(first.Placement.EulerRotation, second.Placement.EulerRotation),
        static (first, second) => ResavedPlacedParts.ScalesDiffer(first.Scale, second.Scale),
        static (first, second) => first.Base != second.Base,
        static (first, second) => first.Placement.Presence != second.Placement.Presence,
    ];

    /// <returns>Null when the game shows every object property as accepted.</returns>
    public static ObjectChange? Decide(PlacedRecordFact record, AcceptedWorld world)
    {
        var versions = record.Versions;
        VersionTrust[] trusts = [.. versions.Select(version => world.TrustOf(version.Plugin))];
        Func<int, int, bool>[] differs =
            [.. ObjectProperties.Select(property => (Func<int, int, bool>)((first, second) => property(versions[first], versions[second])))];
        var acceptances = AcceptedVersions.Decide(trusts, differs);
        return acceptances.Any(acceptance => acceptance.Changed) ? NameTheChange(versions, acceptances) : null;
    }

    /// <summary>
    /// Of the changed properties, the one whose changing version loads last names the change; on a tie, the first in property
    /// order. A property whose shown value only the patch holds has no changing version and names the change only when every
    /// changed property is such, so a real mod's change is never hidden behind an earlier patcher's.
    /// </summary>
    private static ObjectChange NameTheChange(ImmutableArray<PlacedVersionFact> versions, PropertyAcceptance[] acceptances)
    {
        var naming = acceptances.Where(acceptance => acceptance.Changed).MaxBy(acceptance => acceptance.Changing ?? PatchOnlyRank);
        return new ObjectChange(
            naming.Changing is { } changing ? versions[changing].Plugin : null,
            naming.Accepted is { } accepted ? versions[accepted].Plugin : null);
    }
}
