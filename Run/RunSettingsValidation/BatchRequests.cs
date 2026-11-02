using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;

namespace AddedObjectRemover;

/// <summary>Builds the requests of a batch of config files.</summary>
public static class BatchRequests
{
    /// <summary>
    /// Builds every config's request in order. A config file that sets no target, a target that is not in the plugin list, is disabled or loads after the patch, or is the patch
    /// plugin, a mod that requires the target and loads after the patch (unless the switch only to warn is on), or two configs with the same target, fail the whole batch;
    /// a Standard request that stops stays a stop request.
    /// </summary>
    public static BatchRequest BuildAll(
        ConfigsRead read,
        IPluginRecords records,
        LoadOrderPlugins mods,
        bool detailedLogAndReports,
        bool onlyWarnWhenRequiringModLoadsAfterPatch,
        string? reportsFolder,
        int workers) =>
        read switch
        {
            ConfigsRead.Failed failed => new BatchRequest.Failed(failed.Message),
            ConfigsRead.Ready ready => BuildEach(
                ready.Configs, records, mods, detailedLogAndReports, onlyWarnWhenRequiringModLoadsAfterPatch, reportsFolder, workers),
            _ => throw new UnreachableException(),
        };

    /// <summary>The plugin list is read before any settings are checked, so its problem stops every run of the batch.</summary>
    public static BatchRequest.Failed StopOnUnusablePluginList(PluginListProblem problem) =>
        new(
            (problem switch
            {
                PluginListProblem.PathUnknown => "Could not find the game's plugin list file (Plugins.txt in the game's folder under your local application data).",
                PluginListProblem.FileMissing missing => $"The plugin list file {missing.Path} does not exist.",
                PluginListProblem.MalformedLine malformed => $"The plugin list file {malformed.Path} has a malformed entry at line {malformed.Number}. Correct or remove that line.",
                PluginListProblem.PluginNotListed unlisted => $"{unlisted.Plugin} is loaded by Synthesis but is not in the plugin list file {unlisted.Path}.",
                PluginListProblem.PluginListedAfterPatch after =>
                    $"{after.Plugin} is loaded by Synthesis above the patch {after.Patch} but is listed after it in the plugin list file {after.Path}.",
                _ => throw new UnreachableException(),
            })
            + ConfigsRead.NothingWasRun);

    private static BatchRequest BuildEach(
        ImmutableArray<ConfiguredSettings> configs,
        IPluginRecords records,
        LoadOrderPlugins mods,
        bool detailedLogAndReports,
        bool onlyWarnWhenRequiringModLoadsAfterPatch,
        string? reportsFolder,
        int workers)
    {
        var requests = ImmutableArray.CreateBuilder<RunRequest>(configs.Length);
        foreach (var config in configs)
        {
            var request = SettingsValidator.Build(config, records, mods, detailedLogAndReports, onlyWarnWhenRequiringModLoadsAfterPatch, reportsFolder, workers);
            if (DescribeConfigFileStop(request) is { } stopped) return new BatchRequest.Failed(stopped);
            requests.Add(request);
        }

        return FindSharedTarget(requests) is { } shared ? new BatchRequest.Failed(shared) : new BatchRequest.Ready(requests.MoveToImmutable());
    }

    private static string? DescribeConfigFileStop(RunRequest request)
    {
        if (request is not RunRequest.Stop { Source: SettingsSource.ConfigFile file } stop) return null;
        return stop.Reason.Kind switch
        {
            StopKind.NoTargetSet => $"Config file {file.FilePath} sets no mod to clean up (CleanedMod).{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModNotInLoadOrder => $"Config file {file.FilePath}: {stop.Reason.Name} is not in your plugin list.{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModDisabled => $"Config file {file.FilePath}: {stop.Reason.Name} is disabled.{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModNotLoadedBySynthesis => $"Config file {file.FilePath}: {stop.Reason.Name} is enabled in your plugin list but Synthesis did not load it for this group.{ConfigsRead.NothingWasRun}",
            StopKind.CleanedModLoadsAfterPatch when stop.Reason.Placement is { } placement =>
                $"Config file {file.FilePath}: {stop.Reason.Name} loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}): move {placement.Patch} below it.{ConfigsRead.NothingWasRun}",
            StopKind.RequiringModLoadsAfterPatch when stop.Reason.Placement is { } placement =>
                $"Config file {file.FilePath}: {placement.Mod} requires {stop.Reason.Name} and loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}): move {placement.Patch} below it in your plugin list, or turn on \"{RunSettings.OnlyWarnSettingName}\".{ConfigsRead.NothingWasRun}",
            StopKind.TargetIsThePatch => $"Config file {file.FilePath}: {stop.Reason.Name} is the patch this patcher writes.{ConfigsRead.NothingWasRun}",
            _ => throw new UnreachableException(),
        };
    }

    private static string? FindSharedTarget(IEnumerable<RunRequest> requests)
    {
        var filePathByTarget = new Dictionary<PluginName, string>();
        foreach (var ready in requests.OfType<RunRequest.Ready>())
        {
            if (ready.Source is not SettingsSource.ConfigFile file) continue;
            if (!filePathByTarget.TryAdd(ready.Settings.Target, file.FilePath))
            {
                return $"Config files {filePathByTarget[ready.Settings.Target]} and {file.FilePath} both clean up {ready.Settings.Target}; each mod may be cleaned up by one config only.{ConfigsRead.NothingWasRun}";
            }
        }

        return null;
    }
}
