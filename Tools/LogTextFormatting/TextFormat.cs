using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Numbers and durations as log text. Everything follows the current culture.</summary>
public static class TextFormat
{
    private const int SecondsDecimals = 1;

    /// <summary>A whole number with digit grouping, like "12,345".</summary>
    public static string Count<T>(T value) where T : IBinaryInteger<T> => value.ToString("N0", null);

    /// <summary>A number with exactly <paramref name="decimals"/> decimals.</summary>
    public static string Fixed<T>(T value, int decimals) where T : IFormattable => value.ToString($"F{decimals}", null);

    /// <summary>A share where 1 means 100%, rounded to whole percent, written the way the culture writes percents.</summary>
    public static string Percent<T>(T share) where T : IFormattable => share.ToString("P0", null);

    public static string Seconds(TimeSpan elapsed) => Fixed(elapsed.TotalSeconds, SecondsDecimals) + "s";

    /// <summary>The duration as a suffix, like " in 1.2s".</summary>
    public static string InSeconds(TimeSpan elapsed) => $" in {Seconds(elapsed)}";
}
