using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class OtherModsChangedObjectsSettings : IChangedObjectsSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MovedObjects)]
    [SynthesisTooltip("Objects the cleaned mod moved farther than the distance below from where they stood before it.")]
    public MovedObjectSettings Moved { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.ReplacedObjects)]
    [SynthesisTooltip("Objects the cleaned mod left in place (within the distance above) but gave another model.")]
    public ReplacedObjectSettings Replaced { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.CollisionHandling)]
    [SynthesisTooltip(
        "Separate copies of the checks above, with the same defaults; they apply only to other mods' objects the cleaned mod changed.\n"
        + "Used where the cleaned mod put them and, for RestoreIfNoCollision, where they stood before.")]
    public ChangedObjectCollisionSettings Collision { get; set; } = new();

    IMovedObjectsSettingsValues IChangedObjectsSettingsValues.Moved => Moved ?? new();
    IReplacedObjectsSettingsValues IChangedObjectsSettingsValues.Replaced => Replaced ?? new();
    ICollisionSettingsValues IChangedObjectsSettingsValues.Collision => Collision ?? new();
}

public class MovedObjectSettings : IMovedObjectsSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MovedDistance + SettingNames.GameUnitsSuffix)]
    [SynthesisTooltip(
        "Moved this far or less, the object counts as standing where it stood: it is checked only when it got another model and \"Process replaced objects\" is on.\n"
        + "Measured in a straight line, in any direction. 0 or more; 0 counts every move.")]
    public float MovedDistanceInUnits { get; set; } = SettingDefaults.MovedDistanceInUnits;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.WhenRemoved)]
    [SynthesisTooltip(ChangedObjectTexts.WhenRemovedForMoved)]
    public RestoreMode WhenRemoved { get; set; } = SettingDefaults.MovedObjectsWhenRemoved;

    [SynthesisOrder]
    [SynthesisSettingName(ChangedObjectTexts.AttemptResizingName)]
    [SynthesisTooltip(ChangedObjectTexts.AttemptResizingTooltip)]
    public bool AttemptResizingBeforeRemoval { get; set; } = SettingDefaults.ResizeMovedObjects;
}

public class ReplacedObjectSettings : IReplacedObjectsSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Process replaced objects")]
    [SynthesisTooltip(
        "Off: these objects are never checked for removal; the cleaned mod's model stays, unless the area around them was cleared and \"Restore other mods' objects\" is on.\n"
        + "On: they are checked like moved objects, with the two settings below.")]
    public bool Process { get; set; } = SettingDefaults.ProcessReplacedObjects;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.WhenRemoved)]
    [SynthesisTooltip(ChangedObjectTexts.WhenRemovedForReplaced)]
    public RestoreMode WhenRemoved { get; set; } = SettingDefaults.ReplacedObjectsWhenRemoved;

    [SynthesisOrder]
    [SynthesisSettingName(ChangedObjectTexts.AttemptResizingName)]
    [SynthesisTooltip(ChangedObjectTexts.AttemptResizingTooltip + "\nOnly used when \"Process replaced objects\" is on.")]
    public bool AttemptResizingBeforeRemoval { get; set; } = SettingDefaults.ResizeReplacedObjects;
}

internal static class ChangedObjectTexts
{
    public const string AttemptResizingName = "Attempt resizing before removal";

    public const string AttemptResizingTooltip =
        "When it is too close where the cleaned mod put it, first try shrinking or turning it there, within the limits under \"Collision handling\".\n"
        + "If that fits, it stays there; otherwise \"When it would be removed\" decides.";

    public const string WhenRemovedForMoved =
        "Restore: put back what the cleaned mod changed (position, rotation, size, enable state, model); a later change by another plugin stays, and if that is its position it is removed instead.\n"
        + "RestoreIfNoCollision: the same, but only when it does not collide there (\"Collision handling\") with something that exists because of the cleaned mod; otherwise it is removed.\n"
        + "Remove: it is disabled like the cleaned mod's own objects. " + GoesBackByArea;

    public const string WhenRemovedForReplaced =
        "Restore: put back what the cleaned mod changed, its model included; a later change by another plugin stays, and if that is its model it is removed instead.\n"
        + "RestoreIfNoCollision: the same, but only when it does not collide there (as for moved objects); otherwise it is removed. Remove: it is disabled.\n"
        + "Only used when \"Process replaced objects\" is on. " + GoesBackByArea;

    private const string GoesBackByArea =
        "An object that is not removed can still go back when the area around it was cleared (see \"Restore what the cleaned mod changed where it was removed\").";
}

public class ChangedObjectCollisionSettings : ICollisionSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.TooClose)]
    [SynthesisTooltip(
        "The main check, the same as for the cleaned mod's own objects. An object would be removed when an object from another mod comes within a margin around it.\n"
        + "Turn on one or both margins; an object caught by either is removed once. With both off, nothing is removed for being too close.")]
    public TooCloseRemovalSettings TooClose { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.ChangedTerrain)]
    [SynthesisTooltip(
        "The same check as for the cleaned mod's own objects: removes an object when another mod changed the terrain\n"
        + "or water under or around it, so it may float, sink, stand under water or look out of place.\n"
        + "Interiors and worlds without terrain are not checked.")]
    public TerrainChangeSettings TerrainChange { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.ShrinkLimit + SettingNames.PercentOfItsSizeSuffix)]
    [SynthesisTooltip(
        "An object that would be removed for being too close to another mod's object may be shrunk where it stands, down to this share of its size, to make it fit,\n"
        + "in steps of 5%; the largest size that fits is used.\n"
        + "100 or more: objects are never shrunk. At least 1 (a lower value becomes 1). Only used for moved or replaced objects whose \"Attempt resizing before removal\" is on.")]
    public int ShrinkLimitPercent { get; set; } = SettingDefaults.ShrinkLimitPercent;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.TurnLimit + SettingNames.DegreesEitherWaySuffix)]
    [SynthesisTooltip(
        "An object may be turned about its upright axis by up to this many degrees to the left or to the right to make it fit,\n"
        + "in steps of 15 degrees; the smallest turn that fits is used. It is never tilted.\n"
        + "0: objects are never turned. 180 or more: objects may face any way.\n"
        + "Not below 0 (a lower value becomes 0). Only used for moved or replaced objects whose \"Attempt resizing before removal\" is on.")]
    public int TurnLimitDegrees { get; set; } = SettingDefaults.TurnLimitDegrees;

    ITooCloseSettingsValues ICollisionSettingsValues.TooClose => TooClose ?? new TooCloseRemovalSettings();
    ITerrainChangeSettingsValues ICollisionSettingsValues.TerrainChange => TerrainChange ?? new TerrainChangeSettings();
    IShrinkOrTurnSettingsValues ICollisionSettingsValues.ShrinkOrTurn => new ResizeLimitValues(ShrinkLimitPercent, TurnLimitDegrees);

    private sealed record ResizeLimitValues(int ShrinkLimitPercent, int TurnLimitDegrees) : IShrinkOrTurnSettingsValues;
}
