using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Validates the settings against the load order; an invalid value is replaced and reported as a warning, in the order found.</summary>
internal static class OptionsBuilder
{
    private const float MaxSizeMultiplier = 5f;
    private const float MaxTouchDistance = 64f;
    private const float MinSearchRadius = 64f;
    private const float MaxSearchRadius = 8192f;
    private const int MinOtherMastersForPatch = 1;
    private const int MaxOtherMastersForPatch = 100;
    private const float MinPercent = 1f;
    private const int PercentStep = 10;

    /// <summary>Validation runs in a fixed order because the warnings are reported in the order found.</summary>
    public static OptionsResult Build(Settings settings, ModFacts mods, string outputPath, int workers)
    {
        var warnings = new List<SettingWarning>();
        var check = settings.WhatToCheck ?? new CheckSettings();
        if (FindTarget(mods, check.TargetPlugin, out var target, out var targetListing) is { } stop)
        {
            return new OptionsResult.Stop(stop, [.. warnings]);
        }

        var ignore = settings.WhatToIgnore ?? new IgnoreSettings();
        var excluded = ParseExcludedPlugins(ignore.ExcludedPlugins ?? [], warnings);
        WarnIfMastersUnreadable(mods, targetListing, warnings);
        var standing = CreateStandingOptions(mods, targetListing, excluded, ignore, warnings);
        var clash = CreateClashOptions(check, ignore, warnings);
        var followUp = CreateFollowUpOptions(settings.FollowUpRemoval ?? new FollowUpRemovalSettings(), warnings);
        var leftoverSettings = settings.LeftoverInvisibleObjects ?? new LeftoverInvisibleObjectSettings();
        var leftovers = CreateLeftoverOptions(leftoverSettings, warnings);
        var relocation = CreateRelocationOptions(leftoverSettings);
        var detailedLog = settings.Diagnostics?.DetailedLog ?? false;
        var reports = new ReportOptions(
            settings.Diagnostics?.WriteReportFiles ?? false,
            ResolveReportFolder(outputPath, settings, warnings));

        var options = new RunOptions(
            targetListing.Mod,
            target,
            [.. excluded.Select(name => name.ToString())],
            standing,
            CreateReadPlan(followUp.Mode, relocation, detailedLog),
            clash,
            followUp,
            leftoverSettings.RemoveLeftoverInvisibleObjects ? leftovers : null,
            relocation,
            reports,
            detailedLog,
            new Execution(workers));
        return new OptionsResult.Ready(options, [.. warnings]);
    }

    private static StandingOptions CreateStandingOptions(
        ModFacts mods, ModListing targetListing, IReadOnlyList<ModKey> excluded, IgnoreSettings ignore, ICollection<SettingWarning> warnings) =>
        new(
            targetListing.Mod,
            [.. excluded.Select(mods.Mods.Find).OfType<ModRef>()],
            ignore.IgnoreTargetMasters,
            ignore.IgnoreModsPatchedWithTarget,
            ignore.IgnoreModsPatchedWithTarget
                ? Clamp(ignore.MaxOtherMastersForPatch, MinOtherMastersForPatch, MaxOtherMastersForPatch, "maximum other masters for a patch", warnings)
                : ignore.MaxOtherMastersForPatch);

    private static ClashOptions CreateClashOptions(CheckSettings check, IgnoreSettings ignore, ICollection<SettingWarning> warnings)
    {
        var npcs = ValidateDefined(ignore.NpcHandling, IgnoreSettings.DefaultNpcHandling, "NPCs and creatures setting", warnings);
        var sizeMultiplier = Clamp(check.SizeMultiplier, 0, MaxSizeMultiplier, CheckSettings.DefaultSizeMultiplier, "size multiplier", warnings);
        var zoneShape = ValidateDefined(check.ZoneShape, CheckSettings.DefaultZoneShape, "removal zone", warnings);
        return new ClashOptions(sizeMultiplier, zoneShape, npcs);
    }

    private static FollowUpOptions CreateFollowUpOptions(FollowUpRemovalSettings followUp, ICollection<SettingWarning> warnings)
    {
        var mode = ValidateDefined(followUp.Mode, FollowUpRemovalSettings.DefaultMode, "follow-up removal mode", warnings);
        var touchDistance = Clamp(followUp.TouchDistance, 0, MaxTouchDistance, FollowUpRemovalSettings.DefaultTouchDistance, "touch distance", warnings);
        var anchoringPercent = Clamp(
            followUp.AnchoringThresholdPercent, MinPercent, Percent.PerWhole,
            FollowUpRemovalSettings.DefaultAnchoringThresholdPercent, "anchoring threshold", warnings);
        return new FollowUpOptions(mode, touchDistance, anchoringPercent / Percent.PerWhole);
    }

    private static RelocationOptions? CreateRelocationOptions(LeftoverInvisibleObjectSettings leftovers) =>
        leftovers.RemoveLeftoverInvisibleObjects && leftovers.MoveKeptMarkersOutOfOtherModsObjects ? new RelocationOptions() : null;

    /// <summary>A rooted path is used as is; a relative one is resolved against the output plugin's folder.</summary>
    internal static string ResolveReportFolder(string outputPath, Settings settings, ICollection<SettingWarning> warnings)
    {
        var folder = ValidateFolder(settings.Diagnostics?.DiagnosticsFolder?.Trim(), warnings);
        if (Path.IsPathRooted(folder)) return folder;
        var outputDirectory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        return Path.Combine(outputDirectory, folder);
    }

    /// <summary>The surroundings are read only when follow-up removal or relocation needs them; the Editor ID lists only when the detailed log prints them.</summary>
    private static ReadPlan CreateReadPlan(FollowUpRemovalMode mode, RelocationOptions? relocation, bool detailedLog)
    {
        var needsSurroundings = mode == FollowUpRemovalMode.ObjectsSupportedByIt || relocation != null;
        return new ReadPlan(needsSurroundings, needsSurroundings, relocation != null, detailedLog, detailedLog);
    }

    /// <summary>Built (and its settings validated) even when the step is off, so invalid values are always reported.</summary>
    private static LeftoverOptions CreateLeftoverOptions(LeftoverInvisibleObjectSettings leftovers, ICollection<SettingWarning> warnings)
    {
        var preset = ValidateDefined(leftovers.ProtectedTypes, ProtectedInvisibleObjectsPreset.None, "protected types", warnings);
        return new LeftoverOptions(
            LookAround: Clamp(leftovers.SearchRadius, MinSearchRadius, MaxSearchRadius, LeftoverInvisibleObjectSettings.DefaultSearchRadius, "search radius", warnings),
            DirectionClearedPercent: WholeTens(leftovers.DirectionThresholdPercent, "removed area per direction", warnings),
            ClearedDirectionsPercent: WholeTens(leftovers.RemovedDirectionsPercent, "removed directions required", warnings),
            OccupiedDirectionsPercent: WholeTens(leftovers.OccupiedDirectionsPercent, "occupied directions required", warnings),
            NeverRemove: ProtectedInvisibleObjects.Resolve(preset, ValidateKinds(leftovers.CustomProtectedTypes ?? [], warnings)),
            Preset: preset);
    }

    /// <summary>A folder containing characters no path can contain (for example a NUL character) falls back to the default.</summary>
    private static string ValidateFolder(string? configured, ICollection<SettingWarning> warnings)
    {
        if (string.IsNullOrEmpty(configured)) return DiagnosticsSettings.DefaultReportFolder;
        if (configured.IndexOfAny(Path.GetInvalidPathChars()) < 0) return configured;
        warnings.Add(new SettingWarning(
            "report folder",
            $"Warning: report folder '{configured}' contains characters not allowed in a path; using '{DiagnosticsSettings.DefaultReportFolder}'."));
        return DiagnosticsSettings.DefaultReportFolder;
    }

    private static StopReason? FindTarget(ModFacts mods, string? targetPlugin, out ModKey target, out ModListing targetListing)
    {
        target = default;
        targetListing = null!;
        if (string.IsNullOrWhiteSpace(targetPlugin)) return new StopReason(StopKind.NoTargetSet, null);
        if (!ModKey.TryFromNameAndExtension(targetPlugin.Trim(), out target)) return new StopReason(StopKind.InvalidTargetName, targetPlugin);
        var listing = mods.Mods.Find(target) is { } mod ? mods.Find(mod) : null;
        if (listing == null) return new StopReason(StopKind.TargetNotInLoadOrder, target.ToString());
        targetListing = listing;
        return null;
    }

    private static List<ModKey> ParseExcludedPlugins(IEnumerable<string?> names, ICollection<SettingWarning> warnings)
    {
        var excluded = new List<ModKey>();
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (ModKey.TryFromNameAndExtension(name.Trim(), out var key))
            {
                excluded.Add(key);
            }
            else
            {
                warnings.Add(new SettingWarning(
                    "excluded plugins",
                    $"Warning: excluded plugin '{name}' is not a valid plugin file name and was ignored."));
            }
        }
        return excluded;
    }

    private static void WarnIfMastersUnreadable(ModFacts mods, ModListing targetListing, ICollection<SettingWarning> warnings)
    {
        if (targetListing.Loaded) return;
        warnings.Add(new SettingWarning(
            "target plugin",
            $"Warning: could not read {mods.Mods.KeyOf(targetListing.Mod)} to find its masters."));
    }

    private static T ValidateDefined<T>(T value, T fallback, string label, ICollection<SettingWarning> warnings) where T : struct, Enum
    {
        if (Enum.IsDefined(value)) return value;
        warnings.Add(new SettingWarning(label, $"Warning: {label} {value} is invalid; using {fallback}."));
        return fallback;
    }

    private static List<InvisibleObjectKind> ValidateKinds(IEnumerable<InvisibleObjectKind> kinds, ICollection<SettingWarning> warnings)
    {
        var valid = new List<InvisibleObjectKind>();
        foreach (var kind in kinds)
        {
            if (Enum.IsDefined(kind))
            {
                valid.Add(kind);
            }
            else
            {
                warnings.Add(new SettingWarning(
                    "custom protected types",
                    $"Warning: custom protected type {kind} is invalid and was ignored."));
            }
        }
        return valid;
    }

    /// <summary>A percentage limited to 10-100 and rounded to the nearest ten.</summary>
    internal static int WholeTens(int percent, string name, ICollection<SettingWarning> warnings)
    {
        var withinRange = Math.Clamp(percent, PercentStep, Percent.PerWhole);
        if (withinRange != percent)
        {
            warnings.Add(new SettingWarning(name, $"Warning: {name} {percent} is outside {PercentStep}-{Percent.PerWhole}; using {withinRange}."));
        }
        var rounded = (int)Math.Round(withinRange / (double)PercentStep, MidpointRounding.AwayFromZero) * PercentStep;
        if (rounded != withinRange)
        {
            warnings.Add(new SettingWarning(name, $"Warning: {name} {withinRange} is not a multiple of {PercentStep}; using {rounded}."));
        }
        return rounded;
    }

    private static int Clamp(int value, int minimum, int maximum, string name, ICollection<SettingWarning> warnings)
    {
        var clamped = Math.Clamp(value, minimum, maximum);
        if (clamped != value)
        {
            warnings.Add(new SettingWarning(name, $"Warning: {name} {value} is outside {minimum}-{maximum}; using {clamped}."));
        }
        return clamped;
    }

    /// <summary>Out of range values become the nearest valid value; a value that is not a number becomes the default.</summary>
    internal static float Clamp(float value, float minimum, float maximum, float defaultValue, string name, ICollection<SettingWarning> warnings)
    {
        if (float.IsNaN(value))
        {
            warnings.Add(new SettingWarning(name, $"Warning: {name} is not a valid number; using {defaultValue}."));
            return defaultValue;
        }
        if (value >= minimum && value <= maximum) return value;
        var clamped = Math.Clamp(value, minimum, maximum);
        warnings.Add(new SettingWarning(name, $"Warning: {name} {value} is outside {minimum}-{maximum}; using {clamped}."));
        return clamped;
    }
}
