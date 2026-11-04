using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>A record of another plugin that the cleaned mod changed.</summary>
/// <param name="Origin"><see cref="TargetOrigin.Moved"/> or <see cref="TargetOrigin.Replaced"/>.</param>
/// <param name="EarlierVersion">The highest version below the cleaned mod's: what the game shows without the cleaned mod.</param>
internal readonly record struct ChangedObject(TargetOrigin Origin, PlacedVersionFact EarlierVersion);

/// <summary>
/// Whether a record of another plugin that the cleaned mod overrides is a changed object, and which kind, decided property by property
/// with the rule that decides what goes back (<see cref="PartHistory"/>). It is one when it is shown, the version just below the
/// cleaned mod's is not removed and has a position and a rotation, and the property would go back to that version: the cleaned mod
/// moved it when the game shows it farther than the moved distance from that version, and replaced it when it stands at most that far
/// from it with another base object. So a plugin that loads without the cleaned mod and set the position or base since keeps the
/// object as it is, and neither a plugin that needs the cleaned mod nor a patch (this group's or another's) holding the cleaned
/// mod's value does. Any other change (rotation, size, other fields) leaves the object where it stood, serving the purpose other mods' authors expected there.
/// A version in another worldspace or interior than the winner's is no earlier version of the object in this space, so such a record
/// is not a changed object: the old-place check and the target spaces know only the winner's space.
/// </summary>
internal static class ChangedObjectKind
{
    /// <param name="trust">Names the cleaned mod, the patch and the plugins that need the cleaned mod.</param>
    /// <param name="movedDistance">A changed object at most this far from its earlier version stands where it stood.</param>
    /// <returns>Null when the record is not a changed object.</returns>
    public static ChangedObject? Of(PlacedRecordFact record, ModTrustTable trust, float movedDistance)
    {
        if (!record.InPluginSpace || StartsDisabledRule.Of(record.Placement, isWinningTarget: true) != ShownInGame.Present) return null;
        var cleanedModIndex = CleanedModIndexOf(record, trust.CleanedMod);
        if (cleanedModIndex == 0 || cleanedModIndex == record.Versions.Length) return null;
        var earlier = record.Versions[cleanedModIndex - 1];
        if (earlier.SpaceKey != record.Space.Key) return null;
        if (RemovedForm.IsRemoved(earlier.Placement.Presence) || earlier.Placement is not { Position: { } position, EulerRotation: not null }) return null;

        var versions = record.Versions;
        if (Vector3.Distance(position, record.Placement.Position!.Value) > movedDistance)
        {
            return IsTheCleanedModsChange(versions, cleanedModIndex, trust, (lower, higher) => ResavedPlacedParts.PositionsDiffer(lower.Placement.Position, higher.Placement.Position))
                ? new ChangedObject(TargetOrigin.Moved, earlier)
                : null;
        }
        return earlier.Base != record.Base && IsTheCleanedModsChange(versions, cleanedModIndex, trust, (lower, higher) => lower.Base != higher.Base)
            ? new ChangedObject(TargetOrigin.Replaced, earlier)
            : null;
    }

    /// <summary>
    /// The property goes back (<see cref="PartHistory"/>), or a patch (this group's or another group's) changed it again over a value
    /// that would go back: what earlier patchers wrote is judged where they put it, as part of the world the cleaned mod made. The
    /// patches above the cleaned mod's version are left out for that second look; those below it are versions of the earlier world.
    /// </summary>
    /// <param name="differs">Whether the property differs between two versions, the lower first.</param>
    private static bool IsTheCleanedModsChange(
        IReadOnlyList<PlacedVersionFact> versions, int cleanedModIndex, ModTrustTable trust, Func<PlacedVersionFact, PlacedVersionFact, bool> differs)
    {
        PartOutcome OutcomeOver(IReadOnlyList<PlacedVersionFact> read) =>
            PartHistory.Of([.. read.Select(version => version.Plugin)], cleanedModIndex, trust, (lower, higher) => differs(read[lower], read[higher]));

        return OutcomeOver(versions) switch
        {
            PartOutcome.Undo => true,
            PartOutcome.LeftToLaterPlugin later when trust.IsAnyGroupsPatch(later.Plugin) =>
                OutcomeOver([.. versions.Take(cleanedModIndex + 1), .. versions.Skip(cleanedModIndex + 1).Where(version => !trust.IsAnyGroupsPatch(version.Plugin))]) is PartOutcome.Undo,
            _ => false,
        };
    }

    /// <returns>The index of the cleaned mod's version in <see cref="PlacedRecordFact.Versions"/>; their count when it has none there.</returns>
    public static int CleanedModIndexOf(PlacedRecordFact record, PluginName cleanedMod) =>
        record.Versions.TakeWhile(version => version.Plugin != cleanedMod).Count();
}
