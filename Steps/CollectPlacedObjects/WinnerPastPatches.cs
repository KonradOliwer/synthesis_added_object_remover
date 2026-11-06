using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// The plugin whose version of a placed record wins once the versions that only carry what other plugins chose are passed over. This
/// group's patch holds what earlier patchers of the group wrote, so its version is always passed over. Another group's Added Object
/// Remover patch is generated output too: its version is passed over where it only copies the version below it (no object property
/// differs, <see cref="ResavedPlacedParts.ObjectProperties"/>), and it is the plugin that chose the state where it changed one, or
/// where it loads after this patch, since its version then beats what this patch writes.
/// </summary>
internal static class WinnerPastPatches
{
    /// <returns>The plugin that created the record when every version read is passed over: the patches only carry its state.</returns>
    public static PluginName Of(PlacedRecordFact record, ModTrustTable trust)
    {
        var versions = record.Versions;
        for (var index = versions.Length - 1; index >= 0; index--)
        {
            if (!IsPassedOver(versions, index, trust)) return versions[index].Plugin;
        }
        return record.Key.Plugin;
    }

    private static bool IsPassedOver(ImmutableArray<PlacedVersionFact> versions, int index, ModTrustTable trust) =>
        trust.IsPatch(versions[index].Plugin)
        || (trust.IsAnyGroupsPatch(versions[index].Plugin)
            && !trust.LoadsAfterPatch(versions[index].Plugin)
            && index > 0
            && !ResavedPlacedParts.ObjectPropertiesDiffer(versions[index - 1], versions[index]));
}
