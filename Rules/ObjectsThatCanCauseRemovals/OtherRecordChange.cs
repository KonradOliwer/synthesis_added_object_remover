using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals;

/// <summary>
/// Whether the game shows another mod's placed record differently from its accepted values (<see cref="AcceptedVersions"/>), and
/// which plugins the log names for it. Each object property (<see cref="ResavedPlacedParts.ObjectProperties"/>) is decided on its
/// own; the object is changed when any of them is. A property the patch still holds as an earlier run gave it back is accepted
/// (<paramref name="restored"/>).
/// </summary>
/// <param name="restored">The parts earlier runs gave back.</param>
internal sealed class OtherRecordChange(RestoredPartsByRecord restored) : IOtherRecordChange
{
    /// <summary>Ranks a change only the patch holds below every version index, so any real mod's change is named first.</summary>
    private const int PatchOnlyRank = -1;

    public ObjectChange? Decide(PlacedRecordFact record, AcceptedWorld world)
    {
        var versions = record.Versions;
        VersionTrust[] trusts = [.. versions.Select(version => world.TrustOf(version.Plugin))];
        Func<int, int, bool>[] differs =
            [.. ResavedPlacedParts.ObjectProperties.Select(property => (Func<int, int, bool>)((first, second) => ResavedPlacedParts.ObjectPropertyDiffers(property, versions[first], versions[second])))];
        var acceptances = AcceptedVersions.Decide(trusts, differs, GivenBackFrom(record, world.Patch));
        return acceptances.Any(acceptance => acceptance.Changed) ? NameTheChange(versions, acceptances) : null;
    }

    /// <returns>Per object property, the versions whose values the patch was given back; null when none of the record's parts was.</returns>
    private IReadOnlyCollection<int>[]? GivenBackFrom(PlacedRecordFact record, PluginName patch)
    {
        var parts = restored.Of(record.Key).ToArray();
        if (parts.Length == 0) return null;
        PluginName[] plugins = [.. record.Versions.Select(static version => version.Plugin)];
        return [.. ResavedPlacedParts.ObjectProperties.Select(property => AcceptedVersions.GivenBackFrom(plugins, patch, RestoredPartsByRecord.SourcesOf(parts, new RestoredPartName.Placed(property))))];
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
