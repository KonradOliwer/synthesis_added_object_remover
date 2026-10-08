using System.Globalization;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Formats CSV fields: invariant culture, RFC 4180-style quoting of fields that hold a comma, quote or newline.</summary>
public static class CsvFormat
{
    private static readonly char[] CharactersNeedingQuotes = [',', '"', '\n', '\r'];

    public static string Number(float value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The three components as three fields: x, y, z.</summary>
    public static IEnumerable<string> Numbers(Vector3 value) => [Number(value.X), Number(value.Y), Number(value.Z)];

    public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Bool(bool value) => value ? "true" : "false";

    public static string Quote(string value)
    {
        if (value.IndexOfAny(CharactersNeedingQuotes) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>A missing value is an empty field.</summary>
    public static string OptionalText(string? value) => Quote(value ?? string.Empty);
}
