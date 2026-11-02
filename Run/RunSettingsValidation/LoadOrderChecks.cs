using System.Collections.Immutable;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;

namespace AddedObjectRemover;

/// <summary>Checks the cleaned mod and the plugin list against the load order: what stops the run, and what is reported and goes on.</summary>
internal static class LoadOrderChecks
{
    public static CleanedModInLoadOrder FindTarget(IPluginRecords records, LoadOrderPlugins mods, string? targetPlugin)
    {
        if (targetPlugin == null) return new CleanedModInLoadOrder.Stopped(new StopReason(StopKind.NoTargetSet, null));
        var target = ParsePluginName(records, targetPlugin);
        if (target == mods.PatchMod) return new CleanedModInLoadOrder.Stopped(new StopReason(StopKind.TargetIsThePatch, target.ToString()));
        if (FindUnusablePlacement(mods, target) is { } unusable) return new CleanedModInLoadOrder.Stopped(unusable);
        return mods.Find(target) is { } listing
            ? new CleanedModInLoadOrder.Found(target, listing)
            : new CleanedModInLoadOrder.Stopped(new StopReason(StopKind.CleanedModNotLoadedBySynthesis, target.ToString()));
    }

    /// <summary>
    /// Synthesis loads only the enabled plugins above the patch, so the plugin list alone tells why the cleaned mod is missing:
    /// not listed, disabled, or below the patch. The positions are the plugin list's, counted from 0.
    /// </summary>
    private static StopReason? FindUnusablePlacement(LoadOrderPlugins mods, PluginName target)
    {
        var name = target.ToString();
        if (!mods.Positions.Contains(target)) return new StopReason(StopKind.CleanedModNotInLoadOrder, name);
        if (mods.DisabledPlugins.Contains(target)) return new StopReason(StopKind.CleanedModDisabled, name);
        var position = mods.Positions.Of(target);
        return position > mods.PatchPosition
            ? new StopReason(StopKind.CleanedModLoadsAfterPatch, name, new PatchPlacement(mods.PatchMod.ToString(), name, position, mods.PatchPosition))
            : null;
    }

    /// <summary>
    /// The enabled mods that require the cleaned mod and load below the patch, in plugin list order. Overriding a record of the cleaned mod
    /// needs it as a master, so the masters tell every mod that can beat the patch for the cleaned mod's records.
    /// </summary>
    public static ImmutableArray<PatchPlacement> FindRequiringModsAfterPatch(LoadOrderPlugins mods, PluginName cleanedMod) =>
        [.. mods.ListingsRequiring(cleanedMod)
            .Select(listing => new PatchPlacement(mods.PatchMod.ToString(), listing.Mod.ToString(), mods.Positions.Of(listing.Mod), mods.PatchPosition))
            .Where(placement => placement.ModPosition > placement.PatchPosition)
            .OrderBy(placement => placement.ModPosition)];

    public static void WarnOfRequiringModsAfterPatch(ImmutableArray<PatchPlacement> placements, string cleanedMod, ICollection<SettingWarning> warnings)
    {
        foreach (var placement in placements)
        {
            warnings.Add(new SettingWarning(
                RunSettings.OnlyWarnSettingName,
                $"Warning: {placement.Mod} requires {cleanedMod} and loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}); the objects of {cleanedMod} that it overrides are left unchanged."));
        }
    }

    /// <summary>A plugin list entry without its file is a gap in the user's setup: the run goes on without that plugin.</summary>
    public static void WarnOfMissingPlugins(LoadOrderPlugins mods, ICollection<SettingWarning> warnings)
    {
        foreach (var missing in mods.MissingPlugins)
        {
            warnings.Add(new SettingWarning(
                "plugin list",
                $"Warning: {missing} is enabled in your plugin list but its file was not found; it was skipped."));
        }
    }

    public static void WarnIfMastersUnreadable(PluginListing targetListing, ICollection<SettingWarning> warnings)
    {
        if (targetListing.Loaded) return;
        warnings.Add(new SettingWarning(
            SettingNames.CleanedMod,
            $"Warning: could not read {targetListing.Mod} to find its masters."));
    }

    /// <summary>Plugin settings are load-order picker values, which the settings reader only creates from valid plugin file names.</summary>
    public static PluginName ParsePluginName(IPluginRecords records, string name) =>
        records.TryParsePluginName(name, out var parsed) ? parsed : throw new InvalidOperationException($"The plugin setting {name} is not a plugin file name.");
}
