using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class LeftoverVisibleObjectSettings : IVisibleLeftoverSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName("Remove visible objects whose surroundings were removed")]
    [SynthesisTooltip("Turns this removal on. Each round's removals are followed up as \"Also remove what rests on removed objects\" says, and the rounds repeat until nothing more is removed.")]
    public bool RemoveLeftoverVisibleObjects { get; set; } = SettingDefaults.RemoveLeftoverVisibleObjects;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.SurroundingObjects)]
    [SynthesisTooltip(
        "For each visible object, the cleaned mod's other visible objects around it (the cleaned mod's objects, including other mods' objects it changed) are sorted into 8 directions\n"
        + "(north, north-east, ...) and measured by the ground area they cover. Objects removed by the earlier steps, by this step's earlier rounds and by their follow-ups count as removed.\n"
        + "The object is removed when enough of those directions were mostly cleared. The settings are applied in the order listed; the percentages go in steps of 10.")]
    public VisibleSurroundingObjectsSettings SurroundingObjects { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName("Never remove these as visible objects left behind (ticked = kept)")]
    [SynthesisTooltip(
        "Ticked kinds are kept by this step (unless linked to a removed object); unticked kinds can be removed by it. "
        + "They are also kept when an area is restored, even while this step is switched off. The too-close and changed-terrain removals are not affected; \"Also remove what rests on removed objects\" can still remove a ticked kind.\n"
        + "What \"Mod to clean up\" lists as always kept stays kept, ticked or not.")]
    public KeptVisibleObjectSettings AlwaysKeep { get; set; } = new();

    ISurroundingObjectsSettingsValues IVisibleLeftoverSettingsValues.SurroundingObjects => SurroundingObjects ?? new();
    IReadOnlyList<VisibleObjectKind> IVisibleLeftoverSettingsValues.NeverRemove => (AlwaysKeep ?? new()).Kinds();
}

public class VisibleSurroundingObjectsSettings : ISurroundingObjectsSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.SearchRadius + SettingNames.GameUnitsSuffix)]
    [SynthesisTooltip("Other visible objects of the cleaned mod within this distance are its surroundings. 64-8192.")]
    public float SearchRadius { get; set; } = SettingDefaults.SearchRadius;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MinOccupiedDirections)]
    [SynthesisTooltip("A direction has objects when another visible object of the cleaned mod lies that way. With fewer such directions the object is kept. 1-8.")]
    public int MinOccupiedDirections { get; set; } = SettingDefaults.VisibleMinOccupiedDirections;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.DirectionThreshold + SettingNames.PercentSuffix)]
    [SynthesisTooltip("Measured by the ground area the cleaned mod's visible objects cover in that direction. 10-100, in steps of 10.")]
    public int DirectionThresholdPercent { get; set; } = SettingDefaults.VisibleDirectionThresholdPercent;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.ClearedDirections + SettingNames.PercentSuffix)]
    [SynthesisTooltip(
        "Counted only among the directions that have objects, not among all 8. Example: objects lie in 7 directions and 6 of them are cleared: "
        + "6 of 7 is 86%, so it is removed with 80 or less and kept with 90 or more. 10-100, in steps of 10.")]
    public int ClearedDirectionsPercent { get; set; } = SettingDefaults.VisibleClearedDirectionsPercent;
}

public class KeptVisibleObjectSettings
{
    [SynthesisOrder]
    [SynthesisSettingName("NPCs and creatures")]
    [SynthesisTooltip("Placed NPCs and creatures.")]
    public bool NpcsAndCreatures { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Containers")]
    [SynthesisTooltip("Chests, barrels, sacks and other objects that hold items.")]
    public bool Containers { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Furniture")]
    [SynthesisTooltip("Beds, chairs, workbenches and other furniture a character can use.")]
    public bool Furniture { get; set; }

    [SynthesisOrder]
    [SynthesisSettingName("Activators")]
    [SynthesisTooltip("Levers, buttons, pull chains, and other objects the player can activate.")]
    public bool Activators { get; set; }

    /// <summary>The ticked kinds, in the order of <see cref="VisibleObjectKind"/>.</summary>
    public IReadOnlyList<VisibleObjectKind> Kinds() =>
        KindFlags().Where(flag => flag.Ticked).Select(flag => flag.Kind).ToArray();

    private IEnumerable<(VisibleObjectKind Kind, bool Ticked)> KindFlags() =>
    [
        (VisibleObjectKind.NpcsAndCreatures, NpcsAndCreatures),
        (VisibleObjectKind.Containers, Containers),
        (VisibleObjectKind.Furniture, Furniture),
        (VisibleObjectKind.Activators, Activators),
    ];
}
