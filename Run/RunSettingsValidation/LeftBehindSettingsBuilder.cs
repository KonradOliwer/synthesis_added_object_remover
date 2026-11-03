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
    internal const int MinObjectsAroundLowest = 1;
    internal const int MinObjectsAroundHighest = 100;

    /// <summary>The minimum both methods of "Objects left behind" share, validated once.</summary>
    public static int ValidateMinObjectsAround(ILeftoverObjectsSettingsValues values, ICollection<SettingWarning> warnings) =>
        Clamp(values.MinObjectsAround, MinObjectsAroundLowest, MinObjectsAroundHighest, SettingPaths.LeftoverObjectsGroup + SettingNames.MinObjectsAround, warnings);

    /// <summary>Built (and its settings validated) even when the step is off, so invalid values are always reported.</summary>
    /// <param name="minObjectsAround">Validated by <see cref="ValidateMinObjectsAround"/>.</param>
    public static VisibleLeftBehindSettings? CreateVisible(ILeftoverObjectsSettingsValues values, int minObjectsAround, ICollection<SettingWarning> warnings)
    {
        var settings = new VisibleLeftBehindSettings(
            ValidateSurroundings(SettingPaths.VisibleSurroundingObjectsGroup, values.SurroundingObjects, warnings), minObjectsAround);
        return values.RemoveBySurroundingDirections ? settings : null;
    }

    /// <summary>Built (and its settings validated) even when the step is off, so invalid values are always reported.</summary>
    public static InvisibleLeftBehindSettings? CreateInvisible(IInvisibleLeftoverSettingsValues values, ICollection<SettingWarning> warnings)
    {
        var settings = new InvisibleLeftBehindSettings(
            ValidateSurroundings(SettingPaths.InvisibleSurroundingObjectsGroup, values.SurroundingObjects, warnings));
        return values.RemoveLeftoverInvisibleObjects ? settings : null;
    }

    public static NeverRemovedVisibleKinds CreateNeverRemovedVisible(ILeftoverObjectsSettingsValues values) => new(values.NeverRemove.ToHashSet());

    public static NeverRemovedInvisibleKinds CreateNeverRemovedInvisible(IInvisibleLeftoverSettingsValues values) => new(values.NeverRemove.ToHashSet());

    public static MarkerMoveSettings CreateMarkerMoves(IInvisibleLeftoverSettingsValues values) =>
        new(MarkerMoveLimits.MaxDistance, values.MoveKeptMarkersOutOfOtherModsObjects && values.RemoveLeftoverInvisibleObjects);

    private static LeftBehindSurroundings ValidateSurroundings(string group, ISurroundingObjectsSettingsValues values, ICollection<SettingWarning> warnings) =>
        new(
            LookAround: Clamp(values.SearchRadius, MinSearchRadius, MaxSearchRadius, SettingDefaults.SearchRadius, group + SettingNames.SearchRadius, warnings),
            MinOccupiedDirections: Clamp(values.MinOccupiedDirections, MinOccupiedDirectionsLowest, CompassDirections.SectorCount, group + SettingNames.MinOccupiedDirections, warnings),
            DirectionClearedPercent: WholeTens(values.DirectionThresholdPercent, group + SettingNames.DirectionThreshold, warnings),
            ClearedDirectionsPercent: WholeTens(values.ClearedDirectionsPercent, group + SettingNames.ClearedDirections, warnings));
}
