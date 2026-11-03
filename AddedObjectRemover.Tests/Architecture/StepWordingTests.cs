using System.Reflection;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The steps other than the report step, the caches, the ids and the runner hold no user-visible text either: all log wording
/// belongs to the report step. Every member there that holds string literals with a phrase is listed here with its reason;
/// the message of a thrown standard bug exception is never wording and is not looked at. A member is named by its type and
/// its own name, so rewording a message changes nothing here.
/// </summary>
public class StepWordingTests
{
    private static readonly string[] ScannedFolders = ["Steps", "Caches", "Ids", "Run"];
    private static readonly string[] ReportFolders = ["Steps/BuildLogAndReports"];

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
            .Where(field => field.IsLiteral && ToolWordingTests.IsPhrase((string)field.GetRawConstantValue()!))
            .Select(field => KeyValuePair.Create((SettingNamesFile, $"{nameof(SettingNames)}.{field.Name}"), SettingNamesReason));

    /// <summary>The file and the member ("Type.Member") that holds phrase literals, with why they are not log wording of a step.</summary>
    private static readonly Dictionary<(string File, string Place), string> AllowedPhrases = new()
    {
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

    [Fact]
    public void TheStepsCachesIdsAndRunnerHoldNoUserVisibleText()
    {
        var found = SourceFile.All()
            .Where(file => file.IsIn(ScannedFolders) && !file.IsIn(ReportFolders))
            .SelectMany(PhrasePlaces)
            .ToHashSet();
        var allowed = AllowedPhrases.Keys.Concat(SettingNamePhrases.Select(entry => entry.Key)).ToHashSet();

        var notAllowed = found.Except(allowed).Select(Describe).Order(StringComparer.Ordinal).ToList();
        var stale = allowed.Except(found).Select(Describe).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"Text outside the report step; move the wording there:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        AllowListAssert.NoneStale(stale, $"Allowed in {nameof(StepWordingTests)} but no longer found (remove)");
    }

    /// <summary>The file and the member of each string literal that holds a phrase.</summary>
    private static IEnumerable<(string File, string Place)> PhrasePlaces(SourceFile file)
    {
        var phrases = file.WordingLiteralLines.Where(literal => ToolWordingTests.IsPhrase(literal.Literal)).ToList();
        if (phrases.Count == 0) return [];

        var outline = CodeOutline.Of(File.ReadAllText(file.FullPath), file.Path);
        return phrases.Select(phrase => (file.Path, outline.PlaceOfLine(phrase.Line)));
    }

    private static string Describe((string File, string Place) place) => $"{place.File}: {place.Place}";
}
