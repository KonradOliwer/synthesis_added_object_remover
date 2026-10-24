using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// Whether a record of another plugin that the cleaned mod overrides is a moved replacement, judged like the cleaned mod's own
/// objects. A replacement left where the version it replaces stood serves the same purpose as that version, which other mods'
/// authors expected there, so it is not one whatever else the cleaned mod changed (model, rotation, size). It is one when the
/// cleaned mod's version, or the patch's on top of it, wins and stands farther than the moved distance from the replaced version:
/// the highest version below the cleaned mod's. A later plugin that wins the record decides it.
/// </summary>
internal static class MovedReplacement
{
    /// <param name="movedDistance">A replacement at most this far from the version it replaces stands where it stood.</param>
    public static bool Is(PlacedRecordFact record, PluginName cleanedMod, PluginName patch, float movedDistance)
    {
        if (record.WinningMod != cleanedMod && record.WinningMod != patch) return false;
        if (!record.InTargetSpace || StartsDisabledRule.Of(record.Placement, isWinningTarget: true) != ShownInGame.Present) return false;
        if (ReplacedVersionBelow(record, cleanedMod) is not { } replaced) return false;
        if (RemovedForm.IsRemoved(replaced.Placement.Presence) || replaced.Placement.Position is not { } position) return false;
        return Vector3.Distance(position, record.Placement.Position!.Value) > movedDistance;
    }

    /// <returns>Null when the versions kept do not reach below the cleaned mod's.</returns>
    private static PlacedVersionFact? ReplacedVersionBelow(PlacedRecordFact record, PluginName cleanedMod)
    {
        for (var index = 1; index < record.Versions.Length; index++)
        {
            if (record.Versions[index].Plugin == cleanedMod) return record.Versions[index - 1];
        }
        return null;
    }
}
