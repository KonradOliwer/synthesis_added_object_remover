using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>A record of another plugin that the cleaned mod changed.</summary>
/// <param name="Origin"><see cref="TargetOrigin.Moved"/> or <see cref="TargetOrigin.Replaced"/>.</param>
/// <param name="EarlierVersion">The highest version below the cleaned mod's: what the game shows without the cleaned mod.</param>
internal readonly record struct ChangedObject(TargetOrigin Origin, PlacedVersionFact EarlierVersion);

/// <summary>
/// Whether a record of another plugin that the cleaned mod overrides is a changed object, and which kind. It is one when the
/// cleaned mod's version, or the patch's on top of it, wins, is shown, and the version just below the cleaned mod's is not removed
/// and has a position and a rotation: the cleaned mod moved it when the winner stands farther than the moved distance from that
/// version, and replaced it when it stands at most that far from it with another base object. Any other change (rotation, size,
/// other fields) leaves the object where it stood, serving the purpose other mods' authors expected there. A later plugin that wins
/// the record decides it, and so does a fully trusted plugin between the cleaned mod's version and the winner, whether or not the
/// scan walked past it, so how far the scan walked does not change the answer. A plugin that is not fully trusted does not.
/// A version in another worldspace or interior than the winner's is no earlier version of the object in this space, so such a
/// record is not a changed object: the old-place check and the target spaces know only the winner's space.
/// </summary>
internal static class ChangedObjectKind
{
    /// <param name="world">The accepted world, whose patch is the group's output plugin.</param>
    /// <param name="movedDistance">A changed object at most this far from its earlier version stands where it stood.</param>
    /// <returns>Null when the record is not a changed object.</returns>
    public static ChangedObject? Of(PlacedRecordFact record, PluginName cleanedMod, AcceptedWorld world, float movedDistance)
    {
        if (record.WinningMod != cleanedMod && record.WinningMod != world.Patch) return null;
        if (!record.InTargetSpace || StartsDisabledRule.Of(record.Placement, isWinningTarget: true) != ShownInGame.Present) return null;
        if (VersionBelowCleanedMod(record, cleanedMod, world) is not { } earlier) return null;
        if (earlier.SpaceKey != record.Space.Key) return null;
        if (RemovedForm.IsRemoved(earlier.Placement.Presence) || earlier.Placement is not { Position: { } position, EulerRotation: not null }) return null;

        if (Vector3.Distance(position, record.Placement.Position!.Value) > movedDistance) return new ChangedObject(TargetOrigin.Moved, earlier);
        return earlier.Base != record.Base ? new ChangedObject(TargetOrigin.Replaced, earlier) : null;
    }

    /// <returns>Null when the versions kept have no version below the cleaned mod's, or a fully trusted plugin's version is above it.</returns>
    private static PlacedVersionFact? VersionBelowCleanedMod(PlacedRecordFact record, PluginName cleanedMod, AcceptedWorld world)
    {
        var versions = record.Versions;
        var cleanedModIndex = versions.TakeWhile(version => version.Plugin != cleanedMod).Count();
        if (cleanedModIndex == 0 || cleanedModIndex == versions.Length) return null;
        var trustedAboveCleanedMod = versions.Skip(cleanedModIndex + 1).Any(version => world.TrustOf(version.Plugin) == VersionTrust.Full);
        return trustedAboveCleanedMod ? null : versions[cleanedModIndex - 1];
    }
}
