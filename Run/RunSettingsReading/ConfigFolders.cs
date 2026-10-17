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

    public static bool IsFileName(string name) =>
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && IsUsableStem(WithoutExtension(name));

    public static string FileNameOf(string name) =>
        HasExtension(name) ? name : name + Extension;

    /// <summary>Windows drops a trailing space or dot from a folder name, and a name of only dots is a folder reference; the config's report folder is named after it.</summary>
    private static bool IsUsableStem(string stem) => stem.Length > 0 && !stem.EndsWith(' ') && !stem.EndsWith('.');

    private static string WithoutExtension(string name) => HasExtension(name) ? name[..^Extension.Length] : name;

    private static bool HasExtension(string name) => name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
}
