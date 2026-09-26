using System.Globalization;
using System.Text;

namespace AddedObjectRemover;

/// <summary>
/// Diagnostics CSV output: UTF-8, comma-separated, invariant culture, RFC 4180-style quoting of
/// fields that hold a comma, quote or newline.
/// </summary>
internal static class CsvFile
{
    /// <summary>Creates the folder when needed and replaces any existing file.</summary>
    public static void Write(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine(string.Join(",", header));
        foreach (var row in rows) writer.WriteLine(string.Join(",", row));
    }

    public static string Num(float value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Bool(bool value) => value ? "true" : "false";

    /// <summary>CSV-escapes a field: quoted, with embedded quotes doubled, whenever it holds a comma, quote or newline.</summary>
    public static string Text(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
