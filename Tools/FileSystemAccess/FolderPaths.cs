namespace AddedObjectRemover;

public static class FolderPaths
{
    /// <summary>A rooted path is used as is; a relative one is resolved against the base folder.</summary>
    public static string ResolveAgainst(string baseFolder, string folder) =>
        Path.IsPathRooted(folder) ? folder : Path.Combine(baseFolder, folder);
}
