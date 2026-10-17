using System.Collections.Immutable;
using Newtonsoft.Json.Linq;

namespace AddedObjectRemover;

/// <param name="FilePath">The group file's full path.</param>
/// <param name="ConfigFileNames">The config files it lists, as file names, in run order.</param>
internal sealed record GroupFile(string FilePath, ImmutableArray<string> ConfigFileNames)
{
    public static GroupFile Read(string dataFolder, string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new ConfigFilesProblem("Batch mode is on, but no group file name is set (Batch > Group file name).");
        }

        var name = groupName.Trim();
        var folder = ConfigFolders.GroupsFolder(dataFolder);
        if (!ConfigFolders.IsFileName(name))
        {
            throw new ConfigFilesProblem($"Group file name '{name}' must be a file name in {folder}, not a path.");
        }

        var filePath = Path.Combine(folder, ConfigFolders.FileNameOf(name));
        if (!File.Exists(filePath))
        {
            throw new ConfigFilesProblem(
                $"Group file not found: {filePath} (check the name under Batch > Group file name, and that the mod with the folder is enabled in Mod Organizer 2).");
        }

        var root = JsonFileParsing.Parse("Group file", filePath, JsonFileParsing.ReadText("Group file", filePath));
        return new GroupFile(filePath, ConfigFileNamesOf(root, filePath, ConfigFolders.ConfigsFolder(dataFolder)));
    }

    private static ImmutableArray<string> ConfigFileNamesOf(JToken root, string filePath, string configsFolder)
    {
        if (root is not JArray names) throw NotAListOfNames(filePath, root);
        if (names.Count == 0) throw new ConfigFilesProblem($"Group file {filePath} lists no configs.");

        var fileNames = ImmutableArray.CreateBuilder<string>(names.Count);
        foreach (var item in names)
        {
            if (item.Type != JTokenType.String) throw NotAListOfNames(filePath, item);

            var name = item.Value<string>()!;
            if (!ConfigFolders.IsFileName(name))
            {
                throw new ConfigFilesProblem($"Config name '{name}' in group file {filePath} must be a file name in {configsFolder}, not a path.");
            }

            var fileName = ConfigFolders.FileNameOf(name);
            if (fileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                throw new ConfigFilesProblem($"Group file {filePath} lists the config '{name}' twice.");
            }
            fileNames.Add(fileName);
        }
        return fileNames.MoveToImmutable();
    }

    private static ConfigFilesProblem NotAListOfNames(string filePath, JToken found) =>
        new($"Group file {filePath} must hold a list of config names, like [\"first\", \"second\"]; found {JsonFileParsing.DescribeKind(found)} at line {JsonFileParsing.LineOf(found)}.");
}
