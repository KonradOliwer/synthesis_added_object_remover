using System.Reflection;
using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The tools, the steps other than the report step, the rule modules, the caches, the ids and the run hold no user-visible text:
/// all log wording belongs to the report step. Every member there that holds string literals with a phrase is listed here with
/// its reason; the message of a thrown standard bug exception is never wording and is not looked at. A member is named by its
/// type and its own name, so rewording a message changes nothing here. The runner and its run caches hold no string literal at
/// all besides the listed one.
/// </summary>
public partial class WordingTests
{
    internal const string ToolsFolder = "Tools";

    private static readonly string[] ScannedFolders = [ToolsFolder, "Steps", "Rules", "Caches", "Ids", "Run"];
    private static readonly string[] ReportFolders = ["Steps/BuildLogAndReports"];

    private const string BugMessage = "Message of an exception thrown for a bug; it is never log wording.";

    private const string SettingsWarning = "Settings validation words its own corrections of saved settings: the approved \"Warning: …\" lines and the setting names they print.";

    private const string SettingsFailure = "Settings validation words why the saved settings cannot be used; the run stops with this message.";

    private const string SettingNamesReason = "A name or suffix of the settings screen, which the settings validation and the settings classes share.";

    private const string SettingsFailureMessageFile = "Run/RunSettingsReading/SettingsFailureMessage.cs";
    private const string GroupFile = "Run/RunSettingsReading/GroupFile.cs";
    private const string JsonFileParsingFile = "Run/RunSettingsReading/JsonFileParsing.cs";
    private const string BatchFile = "Run/RunSettingsValidation/BatchRequests.cs";
    private const string LoadOrderFile = "Run/RunSettingsValidation/LoadOrderChecks.cs";
    private const string CollisionFile = "Run/RunSettingsValidation/CollisionSettingsBuilder.cs";
    private const string SettingCorrectionsFile = "Run/RunSettingsValidation/SettingCorrections.cs";
    private const string TrustFile = "Run/RunSettingsValidation/TrustSettingsBuilder.cs";
    private const string SettingNamesFile = "Run/RunSettingsValidation.Contracts/SettingNames.cs";

    /// <summary>Every constant of <see cref="SettingNames"/> that holds a phrase.</summary>
    private static readonly IEnumerable<KeyValuePair<(string File, string Place), string>> SettingNamePhrases =
        typeof(SettingNames).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && IsPhrase((string)field.GetRawConstantValue()!))
            .Select(field => KeyValuePair.Create((SettingNamesFile, $"{nameof(SettingNames)}.{field.Name}"), SettingNamesReason));

    /// <summary>The file and the member ("Type.Member") that holds phrase literals, with why they are not log wording of a step.</summary>
    private static readonly Dictionary<(string File, string Place), string> AllowedPhrases = new()
    {
        [("Tools/MeshFileReading/MeshFileSource.cs", "MeshFileSource.GetIniArchiveNames")] =
            "Key of the archive problem (first-record-wins with its kind); the report step's line for this kind never prints it.",
        [("Tools/MeshFileReading/NifGeometryReader.cs", "NifGeometryReader.WarmUpFailedMessage")] = BugMessage,
        [("Tools/DenseIdTables/PerIndexTable.cs", "PerIndexTable.Get")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PluginRecords.cs", "PluginRecords.NotReadMessage")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PatchWriting.cs", "PatchWriting.ForeignRecordsMessage")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/RunLinkCache.cs", "RunLinkCache.ForeignIndexMessage")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/RecordHandles.cs", "RecordHandles.AreaRecordsNotReadMessage")] = BugMessage,
        [("Run/RunSettingsValidation.Contracts/RunSettings.cs", "RunSettings.OnlyWarnSettingName")] = SettingsWarning,
        [("Run/RunSettingsValidation.Contracts/ConfigsRead.cs", "ConfigsRead.NothingWasRun")] = SettingsFailure,
        [(SettingsFailureMessageFile, "SettingsFailureMessage.ConvertedValue")] =
            "Pattern by which settings validation recognises the settings reader's conversion errors; it is never printed.",
        [(SettingsFailureMessageFile, "SettingsFailureMessage.Compose")] = SettingsFailure,
        [(SettingsFailureMessageFile, "SettingsFailureMessage.Describe")] = SettingsFailure,
        [(SettingsFailureMessageFile, "SettingsFailureMessage.DescribeAllowedValues")] = SettingsFailure,
        [(SettingsFailureMessageFile, "SettingsFailureMessage.DescribeUnreadablePluginName")] = SettingsFailure,
        [(SettingsFailureMessageFile, "SettingsFailureMessage.SavedSettingLead")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsLoading.cs", "SettingsLoading.LoadConfigFile")] = SettingsFailure,
        [("Run/RunSettingsReading/ConfigFileReader.cs", "ConfigFileReader.Read")] = SettingsFailure,
        [("Run/RunSettingsReading/ConfigFiles.cs", "ConfigFiles.Read")] = SettingsFailure,
        [(GroupFile, "GroupFile.ConfigFileNamesOf")] = SettingsFailure,
        [(GroupFile, "GroupFile.NotAListOfNames")] = SettingsFailure,
        [(GroupFile, "GroupFile.Read")] = SettingsFailure,
        [(JsonFileParsingFile, "JsonFileParsing.DescribeKind")] = SettingsFailure,
        [(JsonFileParsingFile, "JsonFileParsing.Parse")] = SettingsFailure,
        [(JsonFileParsingFile, "JsonFileParsing.ReadText")] = SettingsFailure,
        [(BatchFile, "BatchRequests.DescribeConfigFileStop")] = SettingsFailure,
        [(BatchFile, "BatchRequests.FindSharedTarget")] = SettingsFailure,
        [(BatchFile, "BatchRequests.StopOnUnusablePluginList")] = SettingsFailure,
        [(CollisionFile, "CollisionSettingsBuilder.ChangedObjectsCollisionTexts")] = SettingsWarning,
        [(CollisionFile, "CollisionSettingsBuilder.CleanedModCollisionTexts")] = SettingsWarning,
        [(CollisionFile, "CollisionSettingsBuilder.CreateChangedTerrainSettings")] = SettingsWarning,
        [(CollisionFile, "CollisionSettingsBuilder.CreateTooCloseSettings")] = SettingsWarning,
        [(CollisionFile, "CollisionSettingsBuilder.NoClashFoundAtEitherPlace")] = SettingsWarning,
        [(LoadOrderFile, "LoadOrderChecks.WarnIfMastersUnreadable")] = SettingsWarning,
        [(LoadOrderFile, "LoadOrderChecks.WarnOfMissingPlugins")] = SettingsWarning,
        [(LoadOrderFile, "LoadOrderChecks.WarnOfRequiringModsAfterPatch")] = SettingsWarning,
        [(SettingCorrectionsFile, "SettingCorrections.AtLeast")] = SettingsWarning,
        [(SettingCorrectionsFile, "SettingCorrections.Clamp")] = SettingsWarning,
        [(SettingCorrectionsFile, "SettingCorrections.DefaultForNotANumber")] = SettingsWarning,
        [(SettingCorrectionsFile, "SettingCorrections.NotNegative")] = SettingsWarning,
        [(SettingCorrectionsFile, "SettingCorrections.ValidateDefined")] = SettingsWarning,
        [(SettingCorrectionsFile, "SettingCorrections.WholeTens")] = SettingsWarning,
        [(TrustFile, "TrustSettingsBuilder.ParseListedPlugins")] = SettingsWarning,
        [(TrustFile, "TrustSettingsBuilder.WarnOfUnlistableNeverTrusted")] = SettingsWarning,
    };

    /// <summary>The literal as written between its quotes, and why it is not user-visible wording.</summary>
    private static readonly Dictionary<string, string> AllowedRunnerLiterals = new()
    {
        ["The NPC {npc} that supplies its own traits was not found."] = "Message of an InvalidOperationException for a bug in the caches.",
    };

    [Fact]
    public void NoUserVisibleTextOutsideTheReportStep()
    {
        var found = SourceFile.All()
            .Where(file => file.IsIn(ScannedFolders) && !file.IsIn(ReportFolders))
            .SelectMany(PhrasePlaces)
            .ToHashSet();
        var allowed = AllowedPhrases.Keys.Concat(SettingNamePhrases.Select(entry => entry.Key)).ToHashSet();

        var notAllowed = found.Except(allowed).Select(Describe).Order(StringComparer.Ordinal).ToList();
        var stale = allowed.Except(found).Select(Describe).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"Text outside the report step; move the wording there:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        AllowListAssert.NoneStale(stale, $"Allowed in {nameof(WordingTests)} but no longer found (remove)");
    }

    [Fact]
    public void TheRunnerHoldsNoStringLiteralBesidesTheListed()
    {
        var found = RunnerRulesTests.RunnerFiles().SelectMany(file => file.StringLiterals).ToHashSet();

        var notAllowed = found.Except(AllowedRunnerLiterals.Keys).Order(StringComparer.Ordinal).ToList();
        var stale = AllowedRunnerLiterals.Keys.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"Text in the runner project; move the wording to the report step:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        AllowListAssert.NoneStale(stale, $"Allowed in {nameof(AllowedRunnerLiterals)} but no longer in the runner or the run caches (remove)");
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

    /// <summary>The file and the member of each string literal that holds a phrase.</summary>
    private static IEnumerable<(string File, string Place)> PhrasePlaces(SourceFile file) =>
        file.WordingLiteralLines.Where(literal => IsPhrase(literal.Literal)).Select(phrase => (file.Path, file.Outline.PlaceOfLine(phrase.Line)));

    private static string Describe((string File, string Place) place) => $"{place.File}: {place.Place}";

    /// <summary>An interpolation hole counts as no word: the guard looks at the fixed text around it.</summary>
    [GeneratedRegex(@"(?<!\{)\{[^{}]*\}")]
    private static partial Regex InterpolationHolePattern();

    /// <summary>A word, then a gap of other characters that holds a space (and no hole), then a word.</summary>
    [GeneratedRegex(@"[A-Za-z]+[^A-Za-z\u0001]* [^A-Za-z\u0001]*[A-Za-z]+")]
    private static partial Regex PhrasePattern();
}
