namespace AddedObjectRemover;

/// <summary>File names written with or without their extension, and the names Windows keeps as written.</summary>
public static class FileNames
{
    public static bool HasExtension(string name, string extension) => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    public static string WithExtension(string name, string extension) => HasExtension(name, extension) ? name : name + extension;

    public static string WithoutExtension(string name, string extension) => HasExtension(name, extension) ? name[..^extension.Length] : name;

    /// <summary>
    /// A plain file name whose stem (the name without the extension) Windows keeps as written: Windows drops a trailing space or dot
    /// from a folder name, and a name of only dots is a folder reference.
    /// </summary>
    public static bool IsKeptAsWritten(string name, string extension) =>
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && WithoutExtension(name, extension) is { Length: > 0 } stem
            && !stem.EndsWith(' ')
            && !stem.EndsWith('.');
}
