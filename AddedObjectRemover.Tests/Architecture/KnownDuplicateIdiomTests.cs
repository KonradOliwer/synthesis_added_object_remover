using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Idioms that were written out by hand in several places and now live in a tool. The business modules use
/// the tool instead. This lists exactly the known duplicates; other copies are the job of the copied-code scan.
/// </summary>
public class KnownDuplicateIdiomTests
{
    private const string ReportFolder = "Steps/BuildLogAndReports";

    private static readonly (string Name, string Tool, Regex Pattern, string[] Places)[] Idioms =
    [
        ("ordinal key-text OrderBy", "RecordKeyTextOrder.Comparer", new(@"\.(?:OrderBy|ThenBy)\([^;]*Key\w*\.ToString\(\)\s*,\s*StringComparer\.Ordinal\b"), []),
        ("percent compare", "SharePercent.AtLeast", new(@"\*\s*Percent\.PerWhole\s*>="), []),
        ("empty text for missing text in a report", "CsvFormat.OptionalText", new(@"\?\?\s*string\.Empty"), [ReportFolder]),
        ("filter by a bool array position", "a tool that picks items by a flag list", new(@"\.Where\(\(_,\s*\w+\)\s*=>[^;]*\[\w+\]"), []),
        ("lookup by target index", "an id lookup built once", new(@"\.ToDictionary\([^;]*TargetIndex"), []),
        ("group, count and rank", "KeyedGroups.CountBy and Rank", new(@"\.GroupBy\([^;]*\.Count\(\)|\.OrderByDescending\(\s*\w+\s*=>\s*\w+\.(?:Value|Count)\b"), []),
    ];

    [Fact]
    public void BusinessCodeUsesTheToolsInsteadOfTheKnownIdioms()
    {
        var found = BusinessCode.SourceFiles()
            .SelectMany(file => Idioms
                .Where(idiom => idiom.Places.Length == 0 || file.IsIn(idiom.Places))
                .Where(idiom => idiom.Pattern.IsMatch(file.Code))
                .Select(idiom => $"{file.Description}: {idiom.Name} (use {idiom.Tool})"))
            .ToList();

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    [Theory]
    [InlineData("rows.OrderBy(row => row.Key.ToString(), StringComparer.Ordinal)", 0)]
    [InlineData("rows.ThenBy(row => row.Other.Key.ToString(), StringComparer.Ordinal)", 0)]
    [InlineData("if (part * Percent.PerWhole >= percent * whole)", 1)]
    [InlineData("var text = value ?? string.Empty;", 2)]
    [InlineData("targets.Where((_, index) => isVisible[index])", 3)]
    [InlineData("targets.ToDictionary(row => row.TargetIndex, row => row)", 4)]
    [InlineData("items.GroupBy(item => item.Space).ToDictionary(group => group.Key, group => group.Count())", 5)]
    [InlineData("counts.OrderByDescending(entry => entry.Value)", 5)]
    public void EveryIdiomPatternFindsItsIdiom(string code, int idiomIndex) =>
        Assert.Matches(Idioms[idiomIndex].Pattern, code);

    [Theory]
    [InlineData("rows.OrderBy(row => row.Name, StringComparer.Ordinal)", 0)]
    [InlineData("rows.OrderBy(row => row.Key, RecordKeyTextOrder.Comparer)", 0)]
    [InlineData("part * percent >= whole", 1)]
    [InlineData("var text = value ?? other;", 2)]
    [InlineData("var text = value ?? \"\";", 2)]
    [InlineData("items.Where(item => item.IsVisible)", 3)]
    [InlineData("items.ToDictionary(item => item.Id)", 4)]
    [InlineData("items.GroupBy(item => item.Space).Select(group => group.Key);", 5)]
    public void EveryIdiomPatternIgnoresNearMisses(string code, int idiomIndex) =>
        Assert.DoesNotMatch(Idioms[idiomIndex].Pattern, code);
}
