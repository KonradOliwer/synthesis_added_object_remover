using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The tools are generic. A business type is any type declared in a step, a cache or the run; while everything is
/// one project these are the files under Steps, Caches and Run.
/// </summary>
public partial class ToolsNameNoBusinessTypeTests
{
    private static readonly string[] BusinessFolders = ["Steps", "Caches", "Run"];

    [Fact]
    public void NoToolNamesATypeOfAStepACacheOrTheRun()
    {
        var tools = SourceFile.All().Where(file => file.IsIn(["Tools"])).ToList();
        var businessTypes = BusinessTypeNames(tools);

        var offenders = tools
            .SelectMany(file => IdentifiersIn(file.Code).Where(businessTypes.Contains).Distinct().Select(name => $"{file.Description}: {name}"))
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheScanFindsBusinessTypesAndTools()
    {
        var files = SourceFile.All();

        var tools = files.Where(file => file.IsIn(["Tools"])).ToList();

        Assert.True(tools.Count > 0);
        Assert.Contains("RemovalDecisions", BusinessTypeNames(tools));
    }

    /// <summary>A name a tool also declares is the tool's own, so it does not count as a business type.</summary>
    private static HashSet<string> BusinessTypeNames(IReadOnlyList<SourceFile> tools)
    {
        var toolTypes = tools.SelectMany(file => SourceFile.DeclaredTypes(file.Code)).ToHashSet();
        return SourceFile.All()
            .Where(file => file.IsIn(BusinessFolders))
            .SelectMany(file => SourceFile.DeclaredTypes(file.Code))
            .Where(name => !toolTypes.Contains(name))
            .ToHashSet();
    }

    private static IEnumerable<string> IdentifiersIn(string code) =>
        IdentifierPattern().Matches(code).Select(match => match.Value);

    [GeneratedRegex(@"\b[A-Z]\w*")]
    private static partial Regex IdentifierPattern();
}
