namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The steps other than the report step, the caches, the ids and the runner hold no user-visible text either: all log wording
/// belongs to the report step. Every string literal there that holds a phrase is listed here with its reason.
/// </summary>
public class StepWordingTests
{
    private static readonly string[] ScannedFolders = ["Steps", "Caches", "Ids", "Run"];
    private static readonly string[] ReportFolders = ["Steps/BuildLogAndReports"];

    private const string BugMessage = "Message of an exception thrown for a bug; it is never log wording.";

    private const string SettingsWarning = "Settings validation words its own corrections of saved settings: the approved \"Warning: …\" lines and the setting names they print.";

    private const string SettingsFailure = "Settings validation words why the saved settings cannot be used; the run stops with this message.";

    /// <summary>The file, the literal as written between its quotes, and why it is not log wording of a step.</summary>
    private static readonly Dictionary<(string File, string Literal), string> AllowedPhrases = new()
    {
        [("Run/RunAllSteps/RunCaches/NpcBodyCache.cs", "The NPC {npc} that supplies its own traits was not found.")] = BugMessage,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {label} {value} is invalid; using {fallback}.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {name} is not a valid number; using {defaultValue}.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {name} {percent} is outside {PercentStep}-{Percent.PerWhole}; using {withinRange}.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {name} {value} is outside {minimum}-{maximum}; using {clamped}.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {name} {withinRange} is not a multiple of {PercentStep}; using {rounded}.")] = SettingsWarning,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", " Value found: {foundValue}.")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "Allowed values: {string.Join(")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", @"Error converting value (.*?) to type|Could not convert string to \w+: (.*?)\.\s*Path|Could not construct ModKey: (.*?)(?: \(Parameter '|[\r\n]|$)")] =
            "Pattern by which settings validation recognises the settings reader's conversion errors; it is never printed.",
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "a plugin name in {settings}")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "Expected a different kind of value.")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "Expected a number.")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "Expected a plugin file name, for example SomeMod.esp.")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "Expected a text value.")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "Expected value: true or false.")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "Invalid saved setting")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsLoading.cs", "Invalid setting in config file {filePath}")] = SettingsFailure,
        [("Run/RunSettingsReading/ConfigFileReader.cs", "Config file")] = SettingsFailure,
        [("Run/RunSettingsReading/ConfigFileReader.cs", "Config file not found: {filePath} (listed in {groupFilePath}). Check the name in the group file, and that the mod with the folder is enabled in Mod Organizer 2.")] = SettingsFailure,
        [("Run/RunSettingsReading/ConfigFileReader.cs", @"Config file {filePath} must hold an object of settings, like {{\""TargetPlugin\"": \""SomeMod.esp\""}}.")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Batch mode is on, but no group file name is set (Batch > Group file name).")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Config name '{name}' in group file {filePath} must be a file name in {configsFolder}, not a path.")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Group file")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Group file name '{name}' must be a file name in {folder}, not a path.")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Group file not found: {filePath} (check the name under Batch > Group file name, and that the mod with the folder is enabled in Mod Organizer 2).")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Group file {filePath} lists no configs.")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Group file {filePath} lists the config '{name}' twice.")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", @"Group file {filePath} must hold a list of config names, like [\""first\"", \""second\""]; found {JsonFileParsing.DescribeKind(found)} at line {JsonFileParsing.LineOf(found)}.")] =
            SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "a list")] = SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "a number")] = SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "an object")] = SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "true or false")] = SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "{kind} {filePath} is not valid JSON: {ex.Message}")] = SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "{kind} {filePath} could not be read: {ex.Message}")] = SettingsFailure,
        [("Run/RunSettingsReading/ConfigFiles.cs", "Mode {settings.Mode} is invalid; use Standard or Batch.{ConfigsRead.NothingWasRun}")] = SettingsFailure,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Config file {file.FilePath}: the mod to clean up, {stop.Reason.Name}, is the patch this patcher writes.{ConfigsRead.NothingWasRun}")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "the settings file")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "the settings file at {location}")] = SettingsFailure,
        [("Run/RunSettingsValidation.Contracts/ConfigsRead.cs", " Nothing was run and no patch was written.")] = SettingsFailure,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Config file {file.FilePath} sets no mod to clean up (TargetPlugin).{ConfigsRead.NothingWasRun}")] = SettingsFailure,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Config file {file.FilePath}: the mod to clean up, {stop.Reason.Name}, is not in the load order.{ConfigsRead.NothingWasRun}")] = SettingsFailure,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Config files {filePathByTarget[ready.Settings.Target]} and {file.FilePath} both clean up {ready.Settings.Target}; each mod may be cleaned up by one config only.{ConfigsRead.NothingWasRun}")] =
            SettingsFailure,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "NPCs and creatures setting")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Warning: could not read {targetListing.Mod} to find its masters.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "The plugin setting {name} is not a plugin file name.")] = BugMessage,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Warning: neither too-close margin is on; no object is removed for being too close.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Warning: changed terrain is on, but neither height nor terrain type changes are counted; no object is removed for changed terrain.")] =
            SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Warning: changed terrain is on, but neither terrain margin is on; no object is removed for changed terrain.")] =
            SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "anchoring threshold")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "changed terrain")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "follow-up removal mode")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "height change tolerance")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "maximum other masters for a patch")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "objects checked for changed terrain")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "occupied directions required")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "removed area per direction")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "removed directions required")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "search radius")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Remove objects too close to other mods' objects > Margin by object size > Margin on every side")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Remove objects too close to other mods' objects > Margin by fixed distance > Margin on every side")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Remove objects on terrain changed by other mods > Margin by object size > Margin on every side")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Remove objects on terrain changed by other mods > Margin by fixed distance > Margin on every side")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "Remove objects on terrain changed by other mods > Remove only when this share of the ground points changed type")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "target plugin")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "too close")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingsValidator.cs", "touch distance")] = SettingsWarning,
        [("Steps/CollectPlacedObjects.Contracts/CollectedObjects.cs", "The support-only objects were not collected in this run.")] = BugMessage,
        [("Steps/RemovalDecisionList/RemovalDecisions.cs", "Only the newest view of the removal decisions can be added to.")] = BugMessage,
        [("Steps/RemovalDecisionList/RemovalDecisions.cs", "The round is not part of this view.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/ExplanationFinder.cs", "Touch rounds with seeds ran for the report files without touch chains.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/ExplanationFinder.cs", "Unknown follow-up removal mode {alsoRemove.Mode}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/RestingObjects.cs", "OnlyWhatLosesItsSupport needs the objects of any plugin.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/RestingObjects.cs", "Unknown follow-up evidence {round.GetType().Name}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/RestingObjects.cs", "Unknown follow-up removal mode {plan.Mode}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/RestingObjects.cs", "Unknown follow-up removal mode {settings.Mode}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/SupportRule.cs", "Unknown supporter type {supporter.Type}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/TargetMeshPaths.cs", "Target {target} has no mesh but was used as a mesh.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/TouchChains.cs", "A touch round has no cause {reason}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/TouchChains.cs", "Touch chains need the touch rounds' search.")] = BugMessage,
        [("Steps/RemoveObjectsOnChangedTerrain/TerrainDifference.cs", "A terrain change without a kind.")] = BugMessage,
        [("Steps/RemoveTooCloseObjects/TooCloseObjects.cs", "Unknown removal zone {settings.Zone}.")] = BugMessage,
        [("Steps/SelectObjectsThatCanCauseRemovals/OtherObjectViews.cs", "Other-mod object {index[slot].Key} is invisible, so it is not active.")] = BugMessage,
    };

    [Fact]
    public void TheStepsCachesIdsAndRunnerHoldNoUserVisibleText()
    {
        var found = SourceFile.All()
            .Where(file => file.IsIn(ScannedFolders) && !file.IsIn(ReportFolders))
            .SelectMany(file => file.StringLiterals.Where(ToolWordingTests.IsPhrase).Select(literal => (File: file.Path, Literal: literal)))
            .ToHashSet();

        var notAllowed = found.Except(AllowedPhrases.Keys).Select(Describe).Order(StringComparer.Ordinal).ToList();
        var stale = AllowedPhrases.Keys.Except(found).Select(Describe).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"Text outside the report step; move the wording there:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        Assert.True(stale.Count == 0, $"Allowed in {nameof(StepWordingTests)} but no longer found (remove):{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
    }

    private static string Describe((string File, string Literal) phrase) => $"{phrase.File}: {phrase.Literal}";
}
