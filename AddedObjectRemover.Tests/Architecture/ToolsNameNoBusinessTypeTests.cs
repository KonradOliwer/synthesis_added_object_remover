using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The tools are generic. A business type is any type declared in a project of a step, a cache or the run, and any
/// type of Ids except the listed record-identity types.
/// </summary>
public partial class ToolsNameNoBusinessTypeTests
{
    private static readonly string[] BusinessFolders = ["Steps", "Caches", "Run"];
    private const string IdsFolder = "Ids";

    /// <summary>The Ids types tools may name, each with why it is not business.</summary>
    private static readonly Dictionary<string, string> RecordIdentityTypes = new()
    {
        ["PluginName"] = "a game format: how a plugin file is named",
        ["RecordKey"] = "a game format: a record's identity, its origin plugin and local FormID",
        ["BaseKey"] = "a game format: the base record a placed record links to",
        ["BaseLinkKind"] = "a game format: the kind of record a base link expects",
        ["RecordKeyOrder"] = "a game format: the order of record keys by plugin name and FormID",
        ["RecordKeyTextOrder"] = "a game format: the order of record keys by their printed text",
    };

    [Fact]
    public void NoToolNamesATypeOfAStepACacheTheRunOrABusinessTypeOfIds()
    {
        var tools = SourceFile.All().Where(file => file.IsIn(["Tools"])).ToList();
        var businessTypes = BusinessTypeNames();

        var offenders = tools
            .SelectMany(file => IdentifiersIn(file.Code).Where(businessTypes.Contains).Distinct().Select(name => $"{file.Description}: {name}"))
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void EveryListedRecordIdentityTypeIsDeclaredInIds()
    {
        var stale = RecordIdentityTypes.Keys.Except(IdsTypeNames()).Order(StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0, $"Listed in {nameof(RecordIdentityTypes)} but not declared in Ids (remove):{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
    }

    [Fact]
    public void TheScanFindsBusinessTypesAndTools()
    {
        var files = SourceFile.All();

        var tools = files.Where(file => file.IsIn(["Tools"])).ToList();

        Assert.True(tools.Count > 0);
        Assert.Contains("RemovalDecisions", BusinessTypeNames());
        Assert.Contains("StepOutcome", BusinessTypeNames());
        Assert.DoesNotContain("RecordKey", BusinessTypeNames());
    }

    private static HashSet<string> BusinessTypeNames() =>
        SourceFile.All()
            .Where(file => file.IsIn(BusinessFolders))
            .SelectMany(file => SourceFile.DeclaredTypes(file.Code))
            .Concat(IdsTypeNames().Except(RecordIdentityTypes.Keys))
            .ToHashSet();

    private static IEnumerable<string> IdsTypeNames() =>
        SourceFile.All().Where(file => file.IsIn([IdsFolder])).SelectMany(file => SourceFile.DeclaredTypes(file.Code));

    private static IEnumerable<string> IdentifiersIn(string code) =>
        IdentifierPattern().Matches(code).Select(match => match.Value);

    [GeneratedRegex(@"\b[A-Z]\w*")]
    private static partial Regex IdentifierPattern();
}
