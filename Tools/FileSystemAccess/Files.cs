namespace AddedObjectRemover;

public static class Files
{
    public static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Deletes the named files in the folder and leaves every other file alone.</summary>
    public static void DeleteListed(string folder, IEnumerable<string> fileNames)
    {
        foreach (var fileName in fileNames) DeleteIfPresent(Path.Combine(folder, fileName));
    }
}
