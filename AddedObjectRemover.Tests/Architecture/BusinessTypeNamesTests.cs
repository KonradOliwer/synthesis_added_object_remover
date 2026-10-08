using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>Business types are named for the domain, never for the data structure or the helper role they play.</summary>
public partial class BusinessTypeNamesTests
{
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["NpcBodySet"] = "The set of meshes that make up one NPC body; the log reports 'body mesh sets'.",
        ["ObjectQueryScratch"] = "The per-worker scratch buffers for questions about the placed objects (the spatial tool's own buffers plus the caller's); reused buffers, not a cache.",
        ["NpcScratch"] = "The design's per-worker scratch buffers for the NPC search (see the tool catalog); reused buffers, not a cache.",
        ["RemovalDecisions.Store"] = "The decision list's own internal versioned store of decisions; the module's data structure.",
        ["ShapeZoneSearch.Scratch"] = "The design's per-worker scratch buffers for the shape-zone search; reused buffers, not a cache.",
        ["TooCloseSearch.Scratch"] = "The design's per-worker scratch buffers for the too-close search; reused buffers, not a cache.",
        ["TouchChainSet"] = "README term: the objects of one touching chain, which are removed together.",
    };

    [Fact]
    public void NoBusinessTypeIsNamedAfterADataStructureOrHelperRole()
    {
        var found = BusinessCode.WrittenTypes()
            .Where(type => BannedName().IsMatch(BusinessCode.NameWithoutArity(type)))
            .Select(BusinessCode.PathName)
            .ToList();

        var unlisted = found.Except(Allowed.Keys).Order(StringComparer.Ordinal).ToList();
        var stale = Allowed.Keys.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0, $"Banned type names:{Environment.NewLine}{string.Join(Environment.NewLine, unlisted)}");
        Assert.True(stale.Count == 0, $"Listed but no longer found:{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
    }

    [Theory]
    [InlineData("TouchCache", true)]
    [InlineData("ItemIndex", true)]
    [InlineData("TextHelpers", true)]
    [InlineData("OrderExtensions", true)]
    [InlineData("LabelFormatting", true)]
    [InlineData("ObjectCollection", true)]
    [InlineData("SpotScratch", true)]
    [InlineData("ProtectedInvisibleObjectsPreset", false)]
    [InlineData("Settings", false)]
    [InlineData("IndexedObject", false)]
    public void TheNamePatternMatchesTheBannedSuffixesAndWords(string name, bool banned) =>
        Assert.Equal(banned, BannedName().IsMatch(name));

    [GeneratedRegex(@"(Cache|Memo|Index|Grid|Helpers?|Utils?|Extensions|Store|Table|Map|Lookup|Set|Scratch|Pool)$|Format|Collection")]
    private static partial Regex BannedName();
}
