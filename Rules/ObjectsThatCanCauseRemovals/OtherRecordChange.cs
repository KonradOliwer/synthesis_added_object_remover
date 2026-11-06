using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals;

/// <summary>
/// Whether the game shows another mod's placed record differently from its accepted values (<see cref="AcceptedVersions"/>), and
/// which plugins the log names for it. Each object property is decided on its own; the object is changed when any of them is. A
/// property the patch still holds as an earlier run gave it back is accepted (<paramref name="restored"/>).
/// </summary>
/// <param name="restored">The parts earlier runs gave back.</param>
internal sealed class OtherRecordChange(RestoredPartsByRecord restored) : IOtherRecordChange
{
    /// <summary>Ranks a change only the patch holds below every version index, so any real mod's change is named first.</summary>
    private const int PatchOnlyRank = -1;

    /// <summary>
    /// The object properties, each as the part a restore gives back and "the two versions differ": position, rotation, scale, base and
    /// presence; base and presence are exact.
    /// </summary>
    private static readonly ImmutableArray<(PlacedParts Part, Func<PlacedVersionFact, PlacedVersionFact, bool> Differs)> ObjectProperties =
    [
        (PlacedParts.Position, static (first, second) => ResavedPlacedParts.PositionsDiffer(first.Placement.Position, second.Placement.Position)),
        (PlacedParts.Rotation, static (first, second) => ResavedPlacedParts.RotationsDiffer(first.Placement.EulerRotation, second.Placement.EulerRotation)),
        (PlacedParts.Scale, static (first, second) => ResavedPlacedParts.ScalesDiffer(first.Scale, second.Scale)),
        (PlacedParts.BaseAndData, static (first, second) => first.Base != second.Base),
        (PlacedParts.Presence, static (first, second) => first.Placement.Presence != second.Placement.Presence),
    ];

    public ObjectChange? Decide(PlacedRecordFact record, AcceptedWorld world)
    {
        var versions = record.Versions;
        VersionTrust[] trusts = [.. versions.Select(version => world.TrustOf(version.Plugin))];
        Func<int, int, bool>[] differs =
            [.. ObjectProperties.Select(property => (Func<int, int, bool>)((first, second) => property.Differs(versions[first], versions[second])))];
        var acceptances = AcceptedVersions.Decide(trusts, differs, GivenBackFrom(record, world.Patch));
        return acceptances.Any(acceptance => acceptance.Changed) ? NameTheChange(versions, acceptances) : null;
    }

    /// <returns>Per object property, the versions whose values the patch was given back; null when none of the record's parts was.</returns>
    private IReadOnlyCollection<int>[]? GivenBackFrom(PlacedRecordFact record, PluginName patch)
    {
        var parts = restored.Of(record.Key).ToArray();
        if (parts.Length == 0) return null;
        PluginName[] plugins = [.. record.Versions.Select(static version => version.Plugin)];
        return [.. ObjectProperties.Select(property => AcceptedVersions.GivenBackFrom(plugins, patch, RestoredPartsByRecord.SourcesOf(parts, new RestoredPartName.Placed(property.Part))))];
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
