using System.Globalization;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Numbers and durations as log text. Decimals and percents follow the current culture; whole numbers never do.</summary>
public static class TextFormat
{
    private const int SecondsDecimals = 1;

    /// <summary>A whole number as plain digits, like "12345", without digit grouping.</summary>
    public static string Count<T>(T value) where T : IBinaryInteger<T> => value.ToString(null, CultureInfo.InvariantCulture);

    /// <summary>A number with exactly <paramref name="decimals"/> decimals.</summary>
    public static string Fixed<T>(T value, int decimals) where T : IFormattable => value.ToString($"F{decimals}", null);

    /// <summary>A share where 1 means 100%, rounded to whole percent, written the way the culture writes percents.</summary>
    public static string Percent<T>(T share) where T : IFormattable => share.ToString("P0", null);

    public static string Seconds(TimeSpan elapsed) => Fixed(elapsed.TotalSeconds, SecondsDecimals) + "s";
}
