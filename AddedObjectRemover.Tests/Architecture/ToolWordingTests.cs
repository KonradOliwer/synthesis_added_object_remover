using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The tools hold no user-visible text: all log wording belongs to the report step. Every string literal under Tools/ that holds
/// a phrase is listed here with its reason; the message of a thrown standard bug exception is never wording and is not looked at.
/// </summary>
public partial class ToolWordingTests
{
    private const string ToolsFolder = "Tools";

    private const string BugMessage = "Message of an exception thrown for a bug; it is never log wording.";

    /// <summary>The file, the literal as written between its quotes, and why it is not user-visible wording.</summary>
    private static readonly Dictionary<(string File, string Literal), string> AllowedPhrases = new()
    {
        [("Tools/MeshFileReading/MeshFileSource.cs", "game INI")] = "Key of the archive problem (first-record-wins with its kind); the report step's line for this kind never prints it.",
        [("Tools/MeshFileReading/NifGeometryReader.cs", "NiflySharp could not load the minimal NIF it had just saved, so its block-type cache was not built and meshes cannot be read safely in parallel.")] = BugMessage,
        [("Tools/DenseIdTables/PerIndexTable.cs", "No item has index {index}.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PluginRecords.cs", "The placed records must be read before they are used.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PatchWriting.cs", "The records to write were not read by this plugin tool.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/RunLinkCache.cs", "The record index was not opened by this plugin tool.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/RecordHandles.cs", "The area record versions were not read; ask for them in the read scope.")] = BugMessage,
    };

    [Fact]
    public void TheToolsHoldNoUserVisibleText()
    {
        var found = SourceFile.All()
            .Where(file => file.IsIn([ToolsFolder]))
            .SelectMany(file => file.WordingLiteralLines.Where(literal => IsPhrase(literal.Literal)).Select(literal => (File: file.Path, literal.Literal)))
            .ToHashSet();

        var notAllowed = found.Except(AllowedPhrases.Keys).Select(Describe).Order(StringComparer.Ordinal).ToList();
        var stale = AllowedPhrases.Keys.Except(found).Select(Describe).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"Text in a tool; move the wording to the report step:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        AllowListAssert.NoneStale(stale, $"Allowed in {nameof(ToolWordingTests)} but no longer in a tool (remove)");
    }

    [Theory]
    [InlineData("  [mesh] planted {path}", true)]
    [InlineData("game INI", true)]
    [InlineData("NIF has no root NiNode.", true)]
    [InlineData(".nif", false)]
    [InlineData("meshes\\", false)]
    [InlineData("data\\meshes\\x.nif", false)]
    [InlineData("{0}, {1}", false)]
    [InlineData("{first} {second}", false)]
    [InlineData("{count} items", false)]
    [InlineData("", false)]
    public void APhraseIsTwoWordsOfLettersSeparatedByASpace(string literal, bool expected) =>
        Assert.Equal(expected, IsPhrase(literal));

    internal static bool IsPhrase(string literal)
    {
        var withoutHoles = InterpolationHolePattern().Replace(literal, "\u0001");
        return PhrasePattern().IsMatch(withoutHoles);
    }

    private static string Describe((string File, string Literal) phrase) => $"{phrase.File}: {phrase.Literal}";

    /// <summary>An interpolation hole counts as no word: the guard looks at the fixed text around it.</summary>
    [GeneratedRegex(@"(?<!\{)\{[^{}]*\}")]
    private static partial Regex InterpolationHolePattern();

    /// <summary>A word, then a gap of other characters that holds a space (and no hole), then a word.</summary>
    [GeneratedRegex(@"[A-Za-z]+[^A-Za-z\u0001]* [^A-Za-z\u0001]*[A-Za-z]+")]
    private static partial Regex PhrasePattern();
}
