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
    /// <returns>The replaced version; null when the record is no moved replacement.</returns>
    public static ReplacedVersion? ReplacedVersionOf(PlacedRecordFact record, PluginName cleanedMod, PluginName patch, float movedDistance)
    {
        if (record.WinningMod != cleanedMod && record.WinningMod != patch) return null;
        if (!record.InTargetSpace || StartsDisabledRule.Of(record.Placement, isWinningTarget: true) != ShownInGame.Present) return null;
        if (ReplacedVersionBelow(record, cleanedMod) is not { } replaced) return null;
        if (RemovedForm.IsRemoved(replaced.Placement.Presence) || replaced.Placement is not { Position: { } position, EulerRotation: { } rotation }) return null;
        if (Vector3.Distance(position, record.Placement.Position!.Value) <= movedDistance) return null;
        return new ReplacedVersion(
            new EulerPlacement(position, rotation, ReferenceScale.Normalize(replaced.Scale)),
            replaced.Location.Cell,
            CanMoveBack(record.Winner, replaced.Location, patch));
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

    /// <remarks>
    /// The patch writes the move back into the replaced version's cell. A record moved to another space, or between a persistent
    /// and a temporary child list (a worldspace's persistent cell and a grid cell), would change what loads it with it; and a
    /// record the patch already holds in another cell would end up twice in the patch. Such a record is not moved back.
    /// </remarks>
    private static bool CanMoveBack(PlacedVersionFact winner, VersionLocation replaced, PluginName patch) =>
        winner.Location.Space == replaced.Space
        && winner.Location.InPersistentList == replaced.InPersistentList
        && (winner.Location.Cell == replaced.Cell || winner.Plugin != patch);
}
