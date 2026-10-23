using Mutagen.Bethesda.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Writes the group file and the config files of Batch mode into the patcher's folder in the Data folder.</summary>
internal static class BatchFiles
{
    private const string PatcherFolder = "synthesis_added_object_remover";
    private const string GroupsFolder = "groups";
    private const string ConfigsFolder = "configs";
    private const string FileExtension = ".json";

    public static void WriteGroup(string dataFolder, string groupName, IReadOnlyList<string> configNames) =>
        Write(dataFolder, GroupsFolder, groupName, "[" + string.Join(", ", configNames.Select(name => $"\"{name}\"")) + "]");

    /// <summary>Writes the settings under their key names, with dropdown choices as text, as a user writes a config file.</summary>
    public static void WriteConfig(string dataFolder, string configName, StandardSettings settings) =>
        Write(dataFolder, ConfigsFolder, configName, JsonConvert.SerializeObject(settings, ConfigJson()));

    private static JsonSerializerSettings ConfigJson()
    {
        var json = new JsonSerializerSettings();
        json.Converters.Add(new StringEnumConverter());
        json.AddMutagenConverters();
        return json;
    }

    private static void Write(string dataFolder, string folder, string name, string content)
    {
        var path = Path.Combine(dataFolder, PatcherFolder, folder, name + FileExtension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
