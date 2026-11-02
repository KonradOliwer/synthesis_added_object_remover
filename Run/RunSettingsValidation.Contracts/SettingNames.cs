namespace AddedObjectRemover.Run.RunSettingsValidation.Contracts;

/// <summary>
/// The names the settings screen shows without their unit suffix, so a warning leads to the setting by the name the screen has.
/// The screen adds the suffix ("(game units)", "(%)", ...) to the setting's name; a group name is the same on the screen and in a warning.
/// </summary>
public static class SettingNames
{
    public const string PathSeparator = " > ";

    public const string GameUnitsSuffix = " (game units)";
    public const string PercentSuffix = " (%)";
    public const string PercentOfItsSizeSuffix = " (% of its size)";
    public const string DegreesEitherWaySuffix = " (degrees either way)";
    public const string TimesObjectSizeSuffix = " (× object size)";

    public const string CleanedMod = "Mod to clean up";
    public const string ChangedObjects = "Other mods' objects the cleaned mod changed";
    public const string MovedObjects = "Moved objects";
    public const string ReplacedObjects = "Replaced objects";
    public const string CollisionHandling = "Collision handling";
    public const string Trust = "Which other mods can cause removals";
    public const string TooClose = "Remove objects too close to other mods' objects";
    public const string ChangedTerrain = "Remove objects on terrain changed by other mods";
    public const string ShrinkOrTurn = "Shrink or turn objects to make them fit";
    public const string AlsoRemove = "Also remove what rests on removed objects";
    public const string VisibleLeftBehind = "Visible objects left behind";
    public const string InvisibleLeftBehind = "Invisible objects left behind";
    public const string SurroundingObjects = "When the cleaned mod's visible objects around it were removed";
    public const string RestoreWhereRemoved = "Restore what the cleaned mod changed where it was removed";
    public const string ClearedArea = "When an area counts as cleared";
    public const string MarginBySize = "Margin by object size";
    public const string MarginByDistance = "Margin by fixed distance";

    public const string MarginOnEverySide = "Margin on every side";
    public const string Npcs = "Other mods' NPCs and creatures cause removal";
    public const string TerrainCheckedObjects = "Which of the cleaned mod's objects are checked";
    public const string HeightChangeTolerance = "Count only height changes larger than";
    public const string WaterHeightChangeTolerance = "Count only water height changes larger than";
    public const string TerrainTypeChangePercent = "Remove only when this share of the ground points changed type";
    public const string WaterCoverChangePercent = "Remove only when this share of the ground got covered/uncovered";
    public const string MovedDistance = "Count as moved when moved farther than";
    public const string WhenRemoved = "When it would be removed";
    public const string ShrinkLimit = "Shrink down to";
    public const string TurnLimit = "Turn by at most";
    public const string FollowUpMode = "When an object is removed, also remove";
    public const string TouchDistance = "Gap that still counts as touching";
    public const string AnchoringThreshold = "OnlyWhatLosesItsSupport: remove when this much support is gone";
    public const string OtherModsObjects = "Other mods' objects left without support";
    public const string SearchRadius = "Look this far around it";
    public const string MinOccupiedDirections = "Decide only if at least this many of the 8 directions have objects";
    public const string DirectionThreshold = "A direction is cleared when this much of it was removed";
    public const string ClearedDirections = "Remove when this share of the directions with objects is cleared";
    public const string ClearedAreaRadius = "Look this far around the place";
    public const string MaxStandingPercent = "Cleared when at most this share still stands";
    public const string Measure = "Measure the share by";
    public const string EdgeBlendDistance = "Blend terrain edges over";
    public const string TrustedMods = "Trusted mods, and their masters";
    public const string OwnChangesMods = "Mods trusted for their own changes only";
    public const string NeverTrustedMods = "Never trusted mods";
    public const string MaxOtherMasters = "...only if it has at most this many other masters";
}
