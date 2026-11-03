using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using static AddedObjectRemover.SettingCorrections;

namespace AddedObjectRemover;

/// <summary>Builds the settings of the two left-behind steps, which judge an object by the surroundings that were removed, and of the marker moves.</summary>
internal static class LeftBehindSettingsBuilder
{
    internal const float MinSearchRadius = 64f;
    internal const float MaxSearchRadius = 8192f;
    internal const int MinOccupiedDirectionsLowest = 1;

    /// <summary>Built (and its settings validated) even when the step is off, so invalid values are always reported.</summary>
    public static VisibleLeftBehindSettings? CreateVisible(IVisibleLeftoverSettingsValues values, NeverRemovedVisibleKinds neverRemove, ICollection<SettingWarning> warnings)
    {
        var settings = new VisibleLeftBehindSettings(
            ValidateSurroundings(SettingPaths.VisibleSurroundingObjectsGroup, values.SurroundingObjects, warnings), neverRemove);
        return values.RemoveLeftoverVisibleObjects ? settings : null;
    }

    /// <summary>Built (and its settings validated) even when the step is off, so invalid values are always reported.</summary>
    public static LeftBehindSettings? CreateInvisible(ILeftoverSettingsValues values, NeverRemovedInvisibleKinds neverRemove, ICollection<SettingWarning> warnings)
    {
        var settings = new LeftBehindSettings(
            ValidateSurroundings(SettingPaths.InvisibleSurroundingObjectsGroup, values.SurroundingObjects, warnings), neverRemove);
        return values.RemoveLeftoverInvisibleObjects ? settings : null;
    }

    public static NeverRemovedVisibleKinds CreateNeverRemovedVisible(IVisibleLeftoverSettingsValues values) => new(values.NeverRemove.ToHashSet());

    public static NeverRemovedInvisibleKinds CreateNeverRemovedInvisible(ILeftoverSettingsValues values) => new(values.NeverRemove.ToHashSet());

    public static MarkerMoveSettings CreateMarkerMoves(ILeftoverSettingsValues values) =>
        new(MarkerMoveLimits.MaxDistance, values.MoveKeptMarkersOutOfOtherModsObjects && values.RemoveLeftoverInvisibleObjects);

    private static LeftBehindSurroundings ValidateSurroundings(string group, ISurroundingObjectsSettingsValues values, ICollection<SettingWarning> warnings) =>
        new(
            LookAround: Clamp(values.SearchRadius, MinSearchRadius, MaxSearchRadius, SettingDefaults.SearchRadius, group + SettingNames.SearchRadius, warnings),
            MinOccupiedDirections: Clamp(values.MinOccupiedDirections, MinOccupiedDirectionsLowest, CompassDirections.SectorCount, group + SettingNames.MinOccupiedDirections, warnings),
            DirectionClearedPercent: WholeTens(values.DirectionThresholdPercent, group + SettingNames.DirectionThreshold, warnings),
            ClearedDirectionsPercent: WholeTens(values.ClearedDirectionsPercent, group + SettingNames.ClearedDirections, warnings));
}
