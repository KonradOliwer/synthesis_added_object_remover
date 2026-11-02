using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover;

internal static class MarginTexts
{
    public const string UseName = "Use this margin";
    public const string UseTooltip = "Turns this margin on or off. When both margins of the group are on, the group acts when either one finds something.";
}

/// <summary>
/// One margin around an object, grown from its size; the look shared by the groups that check an area around each object.
/// Each group has its own subtype that supplies the group's defaults, so a partly written or null group in settings.json
/// still falls back to that group's defaults.
/// </summary>
public abstract class MarginBySizeSettings(bool use, float sizeMultiplier)
{
    [SynthesisOrder]
    [SynthesisSettingName(MarginTexts.UseName)]
    [SynthesisTooltip(MarginTexts.UseTooltip)]
    public bool Use { get; set; } = use;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MarginOnEverySide + SettingNames.TimesObjectSizeSuffix)]
    [SynthesisTooltip(
        "A multiple of the object's own size, added on every side. 0.5 adds half the object's size on every side,\n"
        + "so the checked area is twice as large as the object; 1 adds a whole size on every side (three times as large).\n"
        + "Bigger objects get a bigger margin. 0-5.")]
    public float SizeMultiplier { get; set; } = sizeMultiplier;
}

/// <summary>One margin around an object, a fixed distance; see <see cref="MarginBySizeSettings"/> for the per-group subtypes.</summary>
public abstract class MarginByDistanceSettings(bool use, float distanceInUnits)
{
    [SynthesisOrder]
    [SynthesisSettingName(MarginTexts.UseName)]
    [SynthesisTooltip(MarginTexts.UseTooltip)]
    public bool Use { get; set; } = use;

    [SynthesisOrder]
    [SynthesisSettingName(SettingNames.MarginOnEverySide + SettingNames.GameUnitsSuffix)]
    [SynthesisTooltip("The same margin for every object, whatever its size, added on every side. 0 adds no margin. 0-1024.")]
    public float DistanceInUnits { get; set; } = distanceInUnits;
}

public sealed class TooCloseBySizeSettings() : MarginBySizeSettings(SettingDefaults.RemoveTooCloseBySize, SettingDefaults.SizeMultiplier);

public sealed class TooCloseByDistanceSettings() : MarginByDistanceSettings(SettingDefaults.RemoveTooCloseByDistance, SettingDefaults.RemovalDistanceInUnits);

public sealed class TerrainBySizeSettings() : MarginBySizeSettings(SettingDefaults.TerrainAreaBySize, SettingDefaults.SizeMultiplier);

public sealed class TerrainByDistanceSettings() : MarginByDistanceSettings(SettingDefaults.TerrainAreaByDistance, SettingDefaults.TerrainAreaDistanceInUnits);
