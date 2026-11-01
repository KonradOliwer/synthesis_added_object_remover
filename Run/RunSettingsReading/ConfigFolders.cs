namespace AddedObjectRemover;

/// <summary>Where the group and config files are looked for, and how a name written in a group file or in the settings becomes a file name.</summary>
internal static class ConfigFolders
{
    private const string Root = "synthesis_added_object_remover";
    private const string Groups = "groups";
    private const string Configs = "configs";
    private const string Extension = ".json";

    public static string GroupsFolder(string dataFolder) => Path.Combine(dataFolder, Root, Groups);

    public static string ConfigsFolder(string dataFolder) => Path.Combine(dataFolder, Root, Configs);

    /// <remarks>The config's report folder is named after the name without its extension, so Windows must keep that as written.</remarks>
    public static bool IsFileName(string name) => FileNames.IsKeptAsWritten(name, Extension);

    public static string FileNameOf(string name) => FileNames.WithExtension(name, Extension);
}
