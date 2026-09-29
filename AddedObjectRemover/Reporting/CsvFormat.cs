using System.Globalization;

namespace AddedObjectRemover;

/// <summary>Formats report file fields: invariant culture, RFC 4180-style quoting of fields that hold a comma, quote or newline.</summary>
internal static class CsvFormat
{
    public static string Num(float value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Bool(bool value) => value ? "true" : "false";

    public static string Text(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
