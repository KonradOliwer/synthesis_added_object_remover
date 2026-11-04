using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

public class LeftoverObjectSettings : ILeftoverObjectsSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MinObjectsAround)]
    [SynthesisTooltip(
        "Neither method removes an object unless at least this many of the cleaned mod's other objects, standing or removed, lie within that method's distance. The area method counts only drawn objects. 1-100.")]
    public int MinObjectsAround { get; set; } = SettingDefaults.MinObjectsAround;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.DirectionMethod)]
    [SynthesisTooltip("Turns the direction method on. Each round's removals are followed up as \"Also remove what rests on removed objects\" says, and the rounds repeat until nothing more is removed.")]
    public bool RemoveBySurroundingDirections { get; set; } = SettingDefaults.RemoveBySurroundingDirections;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.DirectionSurroundingObjects)]
    [SynthesisTooltip(
        "For each visible object, the cleaned mod's other visible objects around it (the cleaned mod's objects, including other mods' objects it changed) are sorted into 8 directions\n"
        + "(north, north-east, ...) and measured by the ground area they cover. Objects removed by the earlier steps, by this step's earlier rounds and by their follow-ups count as removed.\n"
        + "The object is removed when enough of those directions were mostly cleared. The settings are applied in the order listed; the percentages go in steps of 10.")]
    public VisibleSurroundingObjectsSettings SurroundingObjects { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.AreaMethod)]
    [SynthesisTooltip(
        "Removes each object of the cleaned mod, visible or invisible, whose area holds almost nothing of the cleaned mod any more.\n"
        + "Every object is judged once, on what the earlier steps removed; also off when a limit below is 100 or more.")]
    public bool RemoveObjectsInClearedAreas { get; set; } = SettingDefaults.RemoveObjectsInClearedAreas;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.AreaMethodClearedArea)]
    [SynthesisTooltip(
        "The area counts as cleared when at most these shares of the cleaned mod's drawn objects there still stand, by size and by number; both must be at most their limits.\n"
        + "If the cleaned mod has no drawn object in the area, the area is not cleared.")]
    public LeftoverClearedAreaSettings ClearedArea { get; set; } = new();

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.NeverRemoveAsLeftoverObjects)]
    [SynthesisTooltip(
        "Ticked kinds are kept by both methods (unless linked to a removed object); unticked kinds can be removed by them. "
        + "Invisible kinds ticked under \"Invisible objects left behind\" are kept by the area method too.\n"
        + "The too-close and changed-terrain removals are not affected; \"Also remove what rests on removed objects\" can still remove a ticked kind. "
        + "What \"Mod to clean up\" lists as always kept stays kept, ticked or not.")]
    public KeptVisibleObjectSettings AlwaysKeep { get; set; } = new();

    ISurroundingObjectsSettingsValues ILeftoverObjectsSettingsValues.SurroundingObjects => SurroundingObjects ?? new();
    IClearedAreaSettingsValues ILeftoverObjectsSettingsValues.ClearedArea => ClearedArea ?? new();
    IReadOnlyList<VisibleObjectKind> ILeftoverObjectsSettingsValues.NeverRemove => (AlwaysKeep ?? new()).Kinds();
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

public class LeftoverClearedAreaSettings : IClearedAreaSettingsValues
{
    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.SearchRadius + SettingNames.GameUnitsSuffix)]
    [SynthesisTooltip(
        "The area is everything this far from the object in every direction, so it is twice this wide. In an interior, the whole interior is one area. 1024-16384.")]
    public float SearchRadius { get; set; } = SettingDefaults.ClearedAreaSearchRadius;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MaxStandingSizePercent + SettingNames.PercentSuffix)]
    [SynthesisTooltip(
        "Each of the cleaned mod's drawn objects weighs by the volume of its box; effect-only objects such as fog add no size.\n"
        + "Decimals are allowed; 100 or more turns the area method off.")]
    public float MaxStandingSizePercent { get; set; } = SettingDefaults.ClearedAreaMaxStandingSizePercent;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MaxStandingCountPercent + SettingNames.PercentSuffix)]
    [SynthesisTooltip(
        "Each drawn object counts once, effect-only ones included.\n"
        + "Decimals are allowed; 100 or more turns the area method off.")]
    public float MaxStandingCountPercent { get; set; } = SettingDefaults.ClearedAreaMaxStandingCountPercent;
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
