using System.Reflection;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The steps other than the report step, the caches, the ids and the runner hold no user-visible text either: all log wording
/// belongs to the report step. Every member there that holds string literals with a phrase is listed here with how many it holds
/// and its reason. A member is named by its type and its own name, so rewording a message changes nothing here.
/// </summary>
public class StepWordingTests
{
    private static readonly string[] ScannedFolders = ["Steps", "Caches", "Ids", "Run"];
    private static readonly string[] ReportFolders = ["Steps/BuildLogAndReports"];

    private const string BugMessage = "Message of an exception thrown for a bug; it is never log wording.";

    private const string SettingsWarning = "Settings validation words its own corrections of saved settings: the approved \"Warning: …\" lines and the setting names they print.";

    private const string SettingsFailure = "Settings validation words why the saved settings cannot be used; the run stops with this message.";

    private const string SettingNamesReason = "A name or suffix of the settings screen, which the settings validation and the settings classes share.";

    private const string BaseObjectShapesFile = "Caches/BaseObjectShapeAndKind/BaseObjectShapes.cs";
    private const string RunSettingsFile = "Run/RunSettingsValidation.Contracts/RunSettings.cs";
    private const string SettingsFailureMessageFile = "Run/RunSettingsReading/SettingsFailureMessage.cs";
    private const string GroupFile = "Run/RunSettingsReading/GroupFile.cs";
    private const string JsonFileParsingFile = "Run/RunSettingsReading/JsonFileParsing.cs";
    private const string BatchFile = "Run/RunSettingsValidation/BatchRequests.cs";
    private const string LoadOrderFile = "Run/RunSettingsValidation/LoadOrderChecks.cs";
    private const string CollisionFile = "Run/RunSettingsValidation/CollisionSettingsBuilder.cs";
    private const string SettingCorrectionsFile = "Run/RunSettingsValidation/SettingCorrections.cs";
    private const string TrustFile = "Run/RunSettingsValidation/TrustSettingsBuilder.cs";
    private const string SettingNamesFile = "Run/RunSettingsValidation.Contracts/SettingNames.cs";
    private const string ChangedObjectJudgingFile = "Steps/JudgeChangedObjects/ChangedObjectJudging.cs";
    private const string TargetsOnTakenAwayFile = "Steps/OtherModsObjectsLeftWithoutSupport/TargetsOnTakenAwayObjects.cs";
    private const string RemovalDecisionsFile = "Steps/RemovalDecisionList/RemovalDecisions.cs";
    private const string RestingFolder = "Steps/RemoveObjectsRestingOnRemovedOnes/";
    private const string LeftBehindFolder = "Steps/RemoveVisibleObjectsLeftBehind/";
    private const string PlacedGuardsFile = "Steps/RestoreWhereRemoved/PlacedGuards.cs";

    /// <summary>Every constant of <see cref="SettingNames"/> that holds a phrase: one literal each.</summary>
    private static readonly IEnumerable<KeyValuePair<(string File, string Place), (int Count, string Reason)>> SettingNamePhrases =
        typeof(SettingNames).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && ToolWordingTests.IsPhrase((string)field.GetRawConstantValue()!))
            .Select(field => KeyValuePair.Create((SettingNamesFile, $"{nameof(SettingNames)}.{field.Name}"), (1, SettingNamesReason)));

    /// <summary>The file, the member ("Type.Member") and how many phrase literals it holds, with why they are not log wording of a step.</summary>
    private static readonly Dictionary<(string File, string Place), (int Count, string Reason)> AllowedPhrases = new()
    {
        [(BaseObjectShapesFile, "BaseObjectShapes.MeasureShape")] = (1, BugMessage),
        [("Run/RunAllSteps/RunCaches/NpcBodyCache.cs", "NpcBodyCache.ReadTraits")] = (1, BugMessage),
        [("Steps/IdentifyTheMods.Contracts/ModTrustTable.cs", "ModTrustTable.VersionTrustOf")] = (1, BugMessage),
        [("Steps/RemovalDecisionList.Contracts/Decision.cs", "Decision.Effect")] = (1, BugMessage),
        [(RunSettingsFile, "RunSettings.CollisionOf")] = (1, BugMessage),
        [(RunSettingsFile, "RunSettings.OnlyWarnSettingName")] = (1, SettingsWarning),
        [("Run/RunSettingsValidation.Contracts/ConfigsRead.cs", "ConfigsRead.NothingWasRun")] = (1, SettingsFailure),
        [(SettingsFailureMessageFile, "SettingsFailureMessage.ConvertedValue")] =
            (1, "Pattern by which settings validation recognises the settings reader's conversion errors; it is never printed."),
        [(SettingsFailureMessageFile, "SettingsFailureMessage.Compose")] = (1, SettingsFailure),
        [(SettingsFailureMessageFile, "SettingsFailureMessage.Describe")] = (2, SettingsFailure),
        [(SettingsFailureMessageFile, "SettingsFailureMessage.DescribeAllowedValues")] = (6, SettingsFailure),
        [(SettingsFailureMessageFile, "SettingsFailureMessage.DescribeUnreadablePluginName")] = (1, SettingsFailure),
        [(SettingsFailureMessageFile, "SettingsFailureMessage.SavedSettingLead")] = (1, SettingsFailure),
        [("Run/RunSettingsReading/SettingsLoading.cs", "SettingsLoading.LoadConfigFile")] = (1, SettingsFailure),
        [("Run/RunSettingsReading/ConfigFileReader.cs", "ConfigFileReader.Read")] = (4, SettingsFailure),
        [("Run/RunSettingsReading/ConfigFiles.cs", "ConfigFiles.Read")] = (1, SettingsFailure),
        [(GroupFile, "GroupFile.ConfigFileNamesOf")] = (3, SettingsFailure),
        [(GroupFile, "GroupFile.NotAListOfNames")] = (1, SettingsFailure),
        [(GroupFile, "GroupFile.Read")] = (5, SettingsFailure),
        [(JsonFileParsingFile, "JsonFileParsing.DescribeKind")] = (4, SettingsFailure),
        [(JsonFileParsingFile, "JsonFileParsing.Parse")] = (1, SettingsFailure),
        [(JsonFileParsingFile, "JsonFileParsing.ReadText")] = (1, SettingsFailure),
        [(BatchFile, "BatchRequests.DescribeConfigFileStop")] = (7, SettingsFailure),
        [(BatchFile, "BatchRequests.FindSharedTarget")] = (1, SettingsFailure),
        [(BatchFile, "BatchRequests.StopOnUnusablePluginList")] = (5, SettingsFailure),
        [(CollisionFile, "CollisionSettingsBuilder.ChangedObjectsCollisionTexts")] = (2, SettingsWarning),
        [(CollisionFile, "CollisionSettingsBuilder.CleanedModCollisionTexts")] = (2, SettingsWarning),
        [(CollisionFile, "CollisionSettingsBuilder.CreateChangedTerrainSettings")] = (4, SettingsWarning),
        [(CollisionFile, "CollisionSettingsBuilder.CreateTooCloseSettings")] = (2, SettingsWarning),
        [(CollisionFile, "CollisionSettingsBuilder.NoClashFoundAtEitherPlace")] = (1, SettingsWarning),
        [(LoadOrderFile, "LoadOrderChecks.ParsePluginName")] = (1, BugMessage),
        [(LoadOrderFile, "LoadOrderChecks.WarnIfMastersUnreadable")] = (1, SettingsWarning),
        [(LoadOrderFile, "LoadOrderChecks.WarnOfMissingPlugins")] = (2, SettingsWarning),
        [(LoadOrderFile, "LoadOrderChecks.WarnOfRequiringModsAfterPatch")] = (1, SettingsWarning),
        [(SettingCorrectionsFile, "SettingCorrections.AtLeast")] = (1, SettingsWarning),
        [(SettingCorrectionsFile, "SettingCorrections.Clamp")] = (2, SettingsWarning),
        [(SettingCorrectionsFile, "SettingCorrections.DefaultForNotANumber")] = (1, SettingsWarning),
        [(SettingCorrectionsFile, "SettingCorrections.NotNegative")] = (2, SettingsWarning),
        [(SettingCorrectionsFile, "SettingCorrections.ValidateDefined")] = (1, SettingsWarning),
        [(SettingCorrectionsFile, "SettingCorrections.WholeTens")] = (2, SettingsWarning),
        [(TrustFile, "TrustSettingsBuilder.ParseListedPlugins")] = (1, SettingsWarning),
        [(TrustFile, "TrustSettingsBuilder.WarnOfUnlistableNeverTrusted")] = (2, SettingsWarning),
        [("Steps/CollectPlacedObjects.Contracts/ScanNeeds.cs", "ScanNeeds.UnionOfAreaRecordVersions")] = (1, BugMessage),
        [("Steps/CollectPlacedObjects/ChangedAreaRecordCollector.cs", "ChangedAreaRecordCollector.CreateEarlierVersion")] = (1, BugMessage),
        [("Steps/CollectPlacedObjects/RecordClassifier.cs", "Classification.Origin")] = (1, BugMessage),
        [(ChangedObjectJudgingFile, "ChangedObjectJudging.CreateOldPlaceWorld")] = (1, BugMessage),
        [(ChangedObjectJudgingFile, "ChangedObjectJudging.EarlierVersionOf")] = (1, BugMessage),
        [(ChangedObjectJudgingFile, "ChangedObjectJudging.ModeOf")] = (1, BugMessage),
        [(ChangedObjectJudgingFile, "ChangedObjectJudging.ShownChangeOf")] = (1, BugMessage),
        [(ChangedObjectJudgingFile, "ChangedObjectJudging.WhenRemovedOf")] = (1, BugMessage),
        [("Steps/OtherModsObjectsLeftWithoutSupport/OtherModsObjectRules.cs", "OtherModsObjectRules.MeshPathOf")] = (2, BugMessage),
        [(TargetsOnTakenAwayFile, "TargetsOnTakenAwayObjects.IsGone")] = (1, BugMessage),
        [(TargetsOnTakenAwayFile, "TargetsOnTakenAwayObjects.Propose")] = (1, BugMessage),
        [(RemovalDecisionsFile, "RemovalDecisions.DecideRemoval")] = (1, BugMessage),
        [(RemovalDecisionsFile, "RemovalDecisions.RequireFittable")] = (1, BugMessage),
        [(RemovalDecisionsFile, "RemovalDecisions.RequireNewestView")] = (1, BugMessage),
        [(RemovalDecisionsFile, "RemovalDecisions.VisibleIndexOf")] = (1, BugMessage),
        [("Steps/RemoveObjectsInClearedAreas/ClearedAreaJudge.cs", "ClearedAreaJudge.Stands")] = (1, BugMessage),
        [(RestingFolder + "ExplanationFinder.cs", "ExplanationFinder.Compute")] = (2, BugMessage),
        [(RestingFolder + "RestingRounds.cs", "RestingRounds.CreateSupportRule")] = (1, BugMessage),
        [(RestingFolder + "RestingRounds.cs", "RestingRounds.PrepareFrom")] = (1, BugMessage),
        [(RestingFolder + "RestingRounds.cs", "RestingRounds.SumPairWork")] = (1, BugMessage),
        [(RestingFolder + "RestingRounds.cs", "RestingRounds.SummarizeSeededRounds")] = (1, BugMessage),
        [(RestingFolder + "SupportRule.cs", "SupportRule.Categorize")] = (1, BugMessage),
        [(RestingFolder + "TargetMeshPaths.cs", "TargetMeshPaths.Get")] = (1, BugMessage),
        [(RestingFolder + "TouchCandidateFinder.cs", "TouchCandidateFinder.BuiltSpaceOf")] = (1, BugMessage),
        [(RestingFolder + "TouchChains.cs", "TouchChains.Find")] = (1, BugMessage),
        [(RestingFolder + "TouchChains.cs", "TouchChains.RecordCause")] = (1, BugMessage),
        [("Steps/RemoveTooCloseObjects/TooCloseObjects.cs", "TooCloseObjects.CreateSearch")] = (1, BugMessage),
        [(LeftBehindFolder + "LeftBehindVerdicts.cs", "LeftBehindVerdicts.AsRestored")] = (1, BugMessage),
        [(LeftBehindFolder + "LeftBehindVerdicts.cs", "LeftBehindVerdicts.JoinRoundDecision")] = (1, BugMessage),
        [(LeftBehindFolder + "VisibleObjectSurroundings.cs", "VisibleObjectSurroundings.RecordFate")] = (1, BugMessage),
        [("Steps/RestoreWhereRemoved/ChangeJudge.cs", "ChangeJudge.KindOf")] = (1, BugMessage),
        [(PlacedGuardsFile, "PlacedGuards.CheckGround")] = (1, BugMessage),
        [(PlacedGuardsFile, "PlacedGuards.CheckTooClose")] = (1, BugMessage),
        [(PlacedGuardsFile, "PlacedGuards.TooCloseRules.For")] = (1, BugMessage),
        [("Steps/SelectObjectsThatCanCauseRemovals/OtherObjectViews.cs", "ObjectsThatCanCauseRemovals.CentreOfVisible")] = (1, BugMessage),
        [("Steps/WriteThePatch/NavmeshOutputCheck.cs", "NavmeshOutputCheck.ThrowOnFault")] = (1, BugMessage),
    };

    [Fact]
    public void TheStepsCachesIdsAndRunnerHoldNoUserVisibleText()
    {
        var found = SourceFile.All()
            .Where(file => file.IsIn(ScannedFolders) && !file.IsIn(ReportFolders))
            .SelectMany(PhrasePlaces)
            .GroupBy(place => place)
            .ToDictionary(group => group.Key, group => group.Count());
        var allowed = AllowedPhrases.Concat(SettingNamePhrases).ToDictionary(entry => entry.Key, entry => entry.Value.Count);

        AllowListAssert.ExactCounts(
            found,
            allowed,
            Describe,
            "Text outside the report step; move the wording there",
            $"Allowed in {nameof(StepWordingTests)} but no longer found (remove)");
    }

    /// <summary>The file and the member of each string literal that holds a phrase.</summary>
    private static IEnumerable<(string File, string Place)> PhrasePlaces(SourceFile file)
    {
        var phrases = file.StringLiteralLines.Where(literal => ToolWordingTests.IsPhrase(literal.Literal)).ToList();
        if (phrases.Count == 0) return [];

        var outline = CodeOutline.Of(File.ReadAllText(file.FullPath), file.Path);
        return phrases.Select(phrase => (file.Path, outline.PlaceOfLine(phrase.Line)));
    }

    private static string Describe((string File, string Place) place, int count) => $"{place.File}: {place.Place} x{count}";
}
