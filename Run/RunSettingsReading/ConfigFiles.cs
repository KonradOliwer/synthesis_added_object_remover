using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AddedObjectRemover;

public static class ConfigFiles
{
    /// <summary>
    /// The settings of every run: in Standard mode the Standard group, in Batch mode the config files of the group file, in its order.
    /// Reading stops at the first problem, which is returned as the failure.
    /// </summary>
    /// <param name="dataFolder">The game's Data folder; the group and config files are looked for under it.</param>
    /// <param name="configSchema">The settings class a config file is read into (the Standard group's).</param>
    /// <param name="addPluginConverters">Adds the converters that read plugin names and form keys, as Synthesis reads settings.json.</param>
    public static ConfigsRead Read(IModeSettingsValues settings, string dataFolder, SettingsSchema configSchema, Action<JsonSerializerSettings> addPluginConverters) =>
        settings.Mode switch
        {
            PatchMode.Standard => new ConfigsRead.Ready([new ConfiguredSettings(new SettingsSource.Standard(), settings.Standard)]),
            PatchMode.Batch => ReadGroup(settings.GroupFileName, dataFolder, configSchema, addPluginConverters),
            _ => new ConfigsRead.Failed($"Mode {settings.Mode} is invalid; use Standard or Batch.{ConfigsRead.NothingWasRun}"),
        };

    private static ConfigsRead ReadGroup(string? groupName, string dataFolder, SettingsSchema configSchema, Action<JsonSerializerSettings> addPluginConverters)
    {
        try
        {
            var group = GroupFile.Read(dataFolder, groupName);
            var reader = new ConfigFileReader(
                configSchema, JsonSettingsLikeSynthesis(addPluginConverters), group.FilePath, ConfigFolders.ConfigsFolder(dataFolder), group.ConfigFileNames.Length);
            return new ConfigsRead.Ready([.. group.ConfigFileNames.Select((fileName, index) => reader.Read(fileName, index + 1))]);
        }
        catch (Exception ex) when (ex is ConfigFilesProblem or SettingsUnreadableException)
        {
            return new ConfigsRead.Failed(ex.Message + ConfigsRead.NothingWasRun);
        }
    }

    /// <summary>Mirrors how Synthesis reads settings.json, so a config reads exactly like the Standard group.</summary>
    private static JsonSerializerSettings JsonSettingsLikeSynthesis(Action<JsonSerializerSettings> addPluginConverters)
    {
        var json = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
        json.Converters.Add(new StringEnumConverter());
        addPluginConverters(json);
        return json;
    }
}
