using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AddedObjectRemover;

/// <summary>Reads the config files one group file lists into the settings class the schema names, by the same JSON rules as settings.json.</summary>
internal sealed class ConfigFileReader(SettingsSchema schema, JsonSerializerSettings json, string groupFilePath, string configsFolder, int configCount)
{
    public ConfiguredSettings Read(string fileName, int number)
    {
        var filePath = Path.Combine(configsFolder, fileName);
        if (!File.Exists(filePath))
        {
            throw new ConfigFilesProblem(
                $"Config file not found: {filePath} (listed in {groupFilePath}). Check the name in the group file, and that the mod with the folder is enabled in Mod Organizer 2.");
        }

        var text = JsonFileParsing.ReadText("Config file", filePath);
        if (JsonFileParsing.Parse("Config file", filePath, text) is not JObject written)
        {
            throw new ConfigFilesProblem($"Config file {filePath} must hold an object of settings, like {{\"TargetPlugin\": \"SomeMod.esp\"}}.");
        }

        // Parsed twice on purpose: only Newtonsoft's own deserialization reports the setting path in a failure message.
        var settings = SettingsLoading.LoadConfigFile(() => (ISettingsValues)JsonConvert.DeserializeObject(text, schema.Root, json)!, schema, filePath);
        var notes = ConfigNotes.Of(written, settings, schema);
        var source = new SettingsSource.ConfigFile(Path.GetFileNameWithoutExtension(fileName), fileName, filePath, number, configCount, notes);
        return new ConfiguredSettings(source, settings);
    }
}
