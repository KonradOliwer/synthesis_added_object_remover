namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Chooses, through the real settings, what a whole run does with other mods' objects the cleaned mod changed. The changed objects'
/// own collision handling has to be set after the standard one is: it is a copy of it as it stands.
/// </summary>
internal static class ChangedObjectsChoice
{
    /// <summary>The changed objects get the too-close and terrain settings and the shrink and turn limits that the standard settings have now.</summary>
    public static Settings WithTheCollisionOfTheCleanedModsObjects(this Settings settings)
    {
        var standard = settings.Standard;
        IShrinkOrTurnSettingsValues limits = standard.ShrinkOrTurn;
        var collision = standard.ChangedObjects.Collision;
        collision.TooClose = standard.TooClose;
        collision.TerrainChange = standard.TerrainChange;
        collision.ShrinkLimitPercent = limits.ShrinkLimitPercent;
        collision.TurnLimitDegrees = limits.TurnLimitDegrees;
        return settings;
    }

    public static Settings WithMovedObjects(this Settings settings, RestoreMode whenRemoved, bool attemptResizing = false)
    {
        var moved = settings.Standard.ChangedObjects.Moved;
        moved.WhenRemoved = whenRemoved;
        moved.AttemptResizingBeforeRemoval = attemptResizing;
        return settings;
    }

    public static Settings WithReplacedObjects(this Settings settings, bool process, RestoreMode whenRemoved, bool attemptResizing = false)
    {
        var replaced = settings.Standard.ChangedObjects.Replaced;
        replaced.Process = process;
        replaced.WhenRemoved = whenRemoved;
        replaced.AttemptResizingBeforeRemoval = attemptResizing;
        return settings;
    }
}
