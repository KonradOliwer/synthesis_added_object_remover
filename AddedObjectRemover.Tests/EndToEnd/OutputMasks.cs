using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Removes what depends on timing, thread count or the machine from a run's output: performance
/// lines are dropped, durations and the run's temporary folder are replaced by placeholders, and the separators
/// of paths below that folder are written as '/' so the text is the same on every platform. Game-relative paths
/// (meshes\aor\crate.nif) are record data and stay untouched.
/// </summary>
internal static partial class OutputMasks
{
    public const string RootPlaceholder = "<root>";
    private static readonly string PathTailPattern =
        $@"(?<tail>(?:[\\/](?:{Regex.Escape(SettingDefaults.ReportFolder)}|[^\s\\/,]+))*)";
    private const string DurationPlaceholder = "in <time>";

    /// <summary>Timing, cache, memory and thread-count lines.</summary>
    public static readonly IReadOnlyList<string> PerformanceLinePrefixes =
    [
        "  Meshes indexed: ",
        "  NPC body cache: ",
        "Done in ",
    ];

    /// <summary>Whether the game INI of the machine running the test can be read.</summary>
    private static readonly string[] MachineLinePrefixes =
    [
        "  Warning: could not read archive list from game INI: ",
    ];

    [GeneratedRegex(@"^Using \d+ threads\.$")]
    public static partial Regex ThreadCount();

    [GeneratedRegex(@"\bin \d+\.\d+s\b")]
    public static partial Regex Duration();

    public static IReadOnlyList<string> MaskLog(IEnumerable<string> lines, string root) =>
        lines
            .Where(line => !ThreadCount().IsMatch(line)
                           && !StartsWithAny(line, PerformanceLinePrefixes)
                           && !StartsWithAny(line, MachineLinePrefixes))
            .Select(line => Duration().Replace(MaskRoot(line, root), DurationPlaceholder))
            .ToList();

    public static IReadOnlyList<string> MaskReport(IEnumerable<string> lines, string root) =>
        lines.Select(line => MaskRoot(line, root)).ToList();

    private static bool StartsWithAny(string line, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal));

    private static string MaskRoot(string line, string root) =>
        Regex.Replace(line, Regex.Escape(root) + PathTailPattern, match => RootPlaceholder + match.Groups["tail"].Value.Replace('\\', '/'), RegexOptions.IgnoreCase);
}
