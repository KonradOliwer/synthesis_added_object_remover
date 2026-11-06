using System.Text;

namespace AddedObjectRemover;

public static class Files
{
    private const string TemporaryNameEnding = ".tmp";

    /// <summary>UTF-8 without a byte-order mark.</summary>
    private static readonly Encoding TextEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Deletes the named files in the folder and leaves every other file alone.</summary>
    public static void DeleteListed(string folder, IEnumerable<string> fileNames)
    {
        foreach (var fileName in fileNames) DeleteIfPresent(Path.Combine(folder, fileName));
    }

    /// <returns>The file's text read as UTF-8; null when there is no such file.</returns>
    public static string? ReadTextIfPresent(string path) => File.Exists(path) ? File.ReadAllText(path, TextEncoding) : null;

    /// <summary>
    /// Writes the text as UTF-8 without a byte-order mark to a temporary file beside the file, then puts it in the file's place, so
    /// a write that stops halfway never leaves a part of the text under the file's name.
    /// </summary>
    public static void ReplaceText(string path, string text)
    {
        var temporary = path + TemporaryNameEnding;
        File.WriteAllText(temporary, text, TextEncoding);
        File.Move(temporary, path, overwrite: true);
    }
}
