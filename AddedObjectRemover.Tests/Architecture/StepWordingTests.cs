using System.Reflection;

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

    private const string BatchFile = "Run/RunSettingsValidation/BatchRequests.cs";
    private const string LoadOrderFile = "Run/RunSettingsValidation/LoadOrderChecks.cs";
    private const string CollisionFile = "Run/RunSettingsValidation/CollisionSettingsBuilder.cs";
    private const string TrustFile = "Run/RunSettingsValidation/TrustSettingsBuilder.cs";
    private const string SettingNamesFile = "Run/RunSettingsValidation.Contracts/SettingNames.cs";

    /// <summary>The names and suffixes of the settings screen, which the settings validation and the settings classes share: every constant of <see cref="SettingNames"/>.</summary>
    private static readonly (string File, string Literal)[] SettingNamePhrases =
    [
        .. typeof(SettingNames).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(ToolWordingTests.IsPhrase)
            .Select(name => (SettingNamesFile, name)),
    ];

    /// <summary>The file, the literal as written between its quotes, and why it is not log wording of a step.</summary>
    private static readonly Dictionary<(string File, string Literal), string> AllowedPhrases = new()
    {
        [("Run/RunAllSteps/RunCaches/NpcBodyCache.cs", "The NPC {npc} that supplies its own traits was not found.")] = BugMessage,
        [("Steps/IdentifyTheMods.Contracts/ModTrustTable.cs", "Unknown trust {unknown}.")] = BugMessage,
        [("Steps/RemovalDecisionList.Contracts/Decision.cs", "Unknown decision {GetType().Name}.")] = BugMessage,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {label} {value} is invalid; using {fallback}.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {name} is not a valid number; using {defaultValue}.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {name} {percent} is outside {PercentStep}-{Percent.PerWhole}; using {withinRange}.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {name} {value} is outside {minimum}-{maximum}; using {clamped}.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {name} {withinRange} is not a multiple of {PercentStep}; using {rounded}.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {name} {value} is below {minimum}; using {minimum}.")] = SettingsWarning,
        [("Run/RunSettingsValidation/SettingCorrections.cs", "Warning: {name} {value} is below 0; using {defaultValue}.")] = SettingsWarning,
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
        [("Run/RunSettingsReading/ConfigFileReader.cs", @"Config file {filePath} must hold an object of settings, like {{\""CleanedMod\"": \""SomeMod.esp\""}}.")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Batch mode is on, but no group file name is set (Batch > Group file name).")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Config name '{name}' in group file {filePath} must be a file name in {configsFolder}, not a path.")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Group file")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Group file name '{name}' must be a file name in {folder}, not a path.")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Group file not found: {filePath} (check the name under Batch > Group file name, and that the mod with the folder is enabled in Mod Organizer 2).")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Group file {filePath} lists no configs.")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", "Group file {filePath} lists the config '{name}' twice.")] = SettingsFailure,
        [("Run/RunSettingsReading/GroupFile.cs", @"Group file {filePath} must hold a list of config names, like [\""first\"", \""second\""]; found {JsonFileParsing.DescribeKind(found)} at line {JsonFiles.LineOf(found)}.")] =
            SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "a list")] = SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "a number")] = SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "an object")] = SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "true or false")] = SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "{kind} {filePath} is not valid JSON: {ex.Message}")] = SettingsFailure,
        [("Run/RunSettingsReading/JsonFileParsing.cs", "{kind} {filePath} could not be read: {ex.Message}")] = SettingsFailure,
        [("Run/RunSettingsReading/ConfigFiles.cs", "Mode {settings.Mode} is invalid; use Standard or Batch.{ConfigsRead.NothingWasRun}")] = SettingsFailure,
        [(BatchFile,"Config file {file.FilePath}: {stop.Reason.Name} is the patch this patcher writes.{ConfigsRead.NothingWasRun}")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "the settings file")] = SettingsFailure,
        [("Run/RunSettingsReading/SettingsFailureMessage.cs", "the settings file at {location}")] = SettingsFailure,
        [("Run/RunSettingsValidation.Contracts/ConfigsRead.cs", " Nothing was run and no patch was written.")] = SettingsFailure,
        [(BatchFile,"Config file {file.FilePath} sets no mod to clean up (CleanedMod).{ConfigsRead.NothingWasRun}")] = SettingsFailure,
        [(BatchFile,"Config file {file.FilePath}: {stop.Reason.Name} is not in your plugin list.{ConfigsRead.NothingWasRun}")] = SettingsFailure,
        [(BatchFile,"Config file {file.FilePath}: {stop.Reason.Name} is disabled.{ConfigsRead.NothingWasRun}")] = SettingsFailure,
        [(BatchFile,"Config file {file.FilePath}: {stop.Reason.Name} is enabled in your plugin list but Synthesis did not load it for this group.{ConfigsRead.NothingWasRun}")] =
            SettingsFailure,
        [(BatchFile,"Config file {file.FilePath}: {stop.Reason.Name} loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}): move {placement.Patch} below it.{ConfigsRead.NothingWasRun}")] =
            SettingsFailure,
        [(BatchFile,@"Config file {file.FilePath}: {placement.Mod} requires {stop.Reason.Name} and loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}): move {placement.Patch} below it in your plugin list, or turn on \""{RunSettings.OnlyWarnSettingName}\"".{ConfigsRead.NothingWasRun}")] =
            SettingsFailure,
        [("Run/RunSettingsValidation.Contracts/RunSettings.cs", "Only warn (don't stop) when a mod that requires the cleaned mod loads after the patch")] = SettingsWarning,
        [(LoadOrderFile,"Warning: {placement.Mod} requires {cleanedMod} and loads after the patch {placement.Patch} (position {placement.ModPosition} > {placement.PatchPosition}); the objects of {cleanedMod} that it overrides are left unchanged.")] =
            SettingsWarning,
        [(BatchFile,"Config files {filePathByTarget[ready.Settings.Target]} and {file.FilePath} both clean up {ready.Settings.Target}; each mod may be cleaned up by one config only.{ConfigsRead.NothingWasRun}")] =
            SettingsFailure,
        [(BatchFile,"Could not find the game's plugin list file (Plugins.txt in the game's folder under your local application data).")] = SettingsFailure,
        [(BatchFile,"The plugin list file {missing.Path} does not exist.")] = SettingsFailure,
        [(BatchFile,"The plugin list file {malformed.Path} has a malformed entry at line {malformed.Number}. Correct or remove that line.")] = SettingsFailure,
        [(BatchFile,"{unlisted.Plugin} is loaded by Synthesis but is not in the plugin list file {unlisted.Path}.")] = SettingsFailure,
        [(BatchFile,"{after.Plugin} is loaded by Synthesis above the patch {after.Patch} but is listed after it in the plugin list file {after.Path}.")] = SettingsFailure,
        [(LoadOrderFile,"Warning: could not read {targetListing.Mod} to find its masters.")] = SettingsWarning,
        [(LoadOrderFile,"Warning: {missing} is enabled in your plugin list but its file was not found; it was skipped.")] = SettingsWarning,
        [(LoadOrderFile,"plugin list")] = SettingsWarning,
        [(LoadOrderFile,"The plugin setting {name} is not a plugin file name.")] = BugMessage,
        [(CollisionFile,"Warning: {groupPath}neither too-close margin is on; {texts.TooCloseEffect}.")] = SettingsWarning,
        [(CollisionFile,"Warning: {groupPath}changed terrain is on, but no height, ground type or water changes are counted; {texts.ChangedTerrainEffect}.")] =
            SettingsWarning,
        [(CollisionFile,"Warning: {groupPath}changed terrain is on, but neither terrain margin is on; {texts.ChangedTerrainEffect}.")] =
            SettingsWarning,
        [(CollisionFile,"no object is removed for being too close")] = SettingsWarning,
        [(CollisionFile,"no object is removed for changed terrain")] = SettingsWarning,
        [(CollisionFile,"no changed object is found too close{NoClashFoundAtEitherPlace}")] = SettingsWarning,
        [(CollisionFile,"no changed object is found on changed terrain{NoClashFoundAtEitherPlace}")] = SettingsWarning,
        [(CollisionFile,", neither where the cleaned mod put it nor at its old place")] = SettingsWarning,
        [(CollisionFile,"changed terrain")] = SettingsWarning,
        [(TrustFile,"Warning: {listName}: {plugin} is not in your plugin list.")] = SettingsWarning,
        [(TrustFile,"Warning: {NeverTrustedModsName}: {plugin} is the mod to clean up, which cannot be listed here; the entry has no effect.")] =
            SettingsWarning,
        [(TrustFile,"Warning: {NeverTrustedModsName}: {plugin} is the patch this patcher writes, which cannot be listed here; the entry has no effect.")] =
            SettingsWarning,
        [(CollisionFile,"too close")] = SettingsWarning,
        [("Run/RunSettingsValidation.Contracts/RunSettings.cs", "Unknown target origin {origin}.")] = BugMessage,
        [("Steps/CollectPlacedObjects.Contracts/ScanNeeds.cs", "Two steps ask for the area record reads.")] = BugMessage,
        [("Steps/CollectPlacedObjects/RecordClassifier.cs", "Unknown record role {Role}.")] = BugMessage,
        [("Steps/CollectPlacedObjects/ChangedAreaRecordCollector.cs", "The reader compared no parts of the record {record.Key}, which the cleaned mod overrides.")] = BugMessage,
        [("Steps/WriteThePatch/NavmeshOutputCheck.cs", "The patch's navmeshes break the navmesh consistency check: {fault}.")] = BugMessage,
        [("Steps/RemovalDecisionList/RemovalDecisions.cs", "Only the newest view of the removal decisions can be added to.")] = BugMessage,
        [("Steps/RemovalDecisionList/RemovalDecisions.cs", "The round is not part of this view.")] = BugMessage,
        [("Steps/RemoveVisibleObjectsLeftBehind/LeftBehindVerdicts.cs", "Unexpected round effect {effect} in a left-behind round.")] = BugMessage,
        [("Steps/RemoveVisibleObjectsLeftBehind/VisibleObjectSurroundings.cs", "Target {gone.Index} left its place twice.")] = BugMessage,
        [("Steps/RestoreWhereRemoved/ClearedAreaJudge.cs", "The cleaned mod's own object {target} has the fate {fate}; only another plugin's object goes back.")] = BugMessage,
        [("Steps/RestoreWhereRemoved/ChangeJudge.cs", "The placed record {change.Key} goes back with {undo}, which is no restored kind.")] = BugMessage,
        [("Steps/RestoreWhereRemoved/PlacedGuards.cs", "The too-close rule gave the reason {other}.")] = BugMessage,
        [("Steps/RestoreWhereRemoved/PlacedGuards.cs", "The ground of cell {changed.Cell} stays changed, but no LAND record of it wins.")] = BugMessage,
        [("Steps/RemoveVisibleObjectsLeftBehind/LeftBehindVerdicts.cs", "The left-behind rule did not propose to remove an object with the outcome {proposed}.")] = BugMessage,
        [("Steps/RemovalDecisionList/RemovalDecisions.cs", "Unknown entry {unknown} in the when-removed table.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/ExplanationFinder.cs", "Touch rounds with seeds ran for the report files without touch chains.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/ExplanationFinder.cs", "Unknown follow-up removal mode {alsoRemove.Mode}.")] = BugMessage,
        [("Steps/OtherModsObjectsLeftWithoutSupport/OtherModsObjectRules.cs", "The other mod's object {other.Key} has no mesh but was judged.")] = BugMessage,
        [("Steps/OtherModsObjectsLeftWithoutSupport/OtherModsObjectRules.cs", "The target object {target.Key} has no mesh but was asked for its support.")] = BugMessage,
        [("Steps/OtherModsObjectsLeftWithoutSupport/TargetsOnTakenAwayObjects.cs", "Unknown follow-up removal mode {settings.Mode}.")] = BugMessage,
        [("Steps/OtherModsObjectsLeftWithoutSupport/TargetsOnTakenAwayObjects.cs", "Unknown supporter type {supporter.Type}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/RestingRounds.cs", "OnlyWhatLosesItsSupport needs the objects of any plugin.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/RestingRounds.cs", "Unknown follow-up evidence {round.GetType().Name}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/RestingRounds.cs", "Unknown follow-up removal mode {mode}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/RestingRounds.cs", "Unknown follow-up removal mode {settings.Mode}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/SupportRule.cs", "Unknown supporter type {supporter.Type}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/TargetMeshPaths.cs", "Target {target} has no mesh but was used as a mesh.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/TouchCandidateFinder.cs", "Target {target} takes no part in the touch search.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/TouchChains.cs", "A touch round has no cause {reason}.")] = BugMessage,
        [("Steps/RemoveObjectsRestingOnRemovedOnes/TouchChains.cs", "Touch chains need the touch rounds' search.")] = BugMessage,
        [("Steps/RemoveTooCloseObjects/TooCloseObjects.cs", "Unknown removal zone {zone}.")] = BugMessage,
        [("Steps/RemovalDecisionList/RemovalDecisions.cs", "Target {target.Index} cannot be fitted: it is decided, protected or linked.")] = BugMessage,
        [("Steps/SelectObjectsThatCanCauseRemovals/OtherObjectViews.cs", "Other-mod object {index[slot].Key} is invisible, so it is not active.")] = BugMessage,
        [("Steps/JudgeChangedObjects/ChangedObjectJudging.cs", "Target {target.Key} is not a changed object, so it has no earlier version.")] = BugMessage,
        [("Steps/JudgeChangedObjects/ChangedObjectJudging.cs", "Unknown restore mode {mode}.")] = BugMessage,
        [("Steps/JudgeChangedObjects/ChangedObjectJudging.cs", "Unknown target origin {target.Origin}.")] = BugMessage,
        [("Steps/JudgeChangedObjects/ChangedObjectJudging.cs", "Target {target.Key} of origin {target.Origin} is not a changed object.")] = BugMessage,
        [("Steps/JudgeChangedObjects/ChangedObjectJudging.cs", "The old-place world was asked for, but the scan did not judge the old places.")] = BugMessage,
    };

    [Fact]
    public void TheStepsCachesIdsAndRunnerHoldNoUserVisibleText()
    {
        var found = SourceFile.All()
            .Where(file => file.IsIn(ScannedFolders) && !file.IsIn(ReportFolders))
            .SelectMany(file => file.StringLiterals.Where(ToolWordingTests.IsPhrase).Select(literal => (File: file.Path, Literal: literal)))
            .ToHashSet();

        var allowed = AllowedPhrases.Keys.Concat(SettingNamePhrases).ToList();
        var notAllowed = found.Except(allowed).Select(Describe).Order(StringComparer.Ordinal).ToList();
        var stale = allowed.Except(found).Select(Describe).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"Text outside the report step; move the wording there:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        AllowListAssert.NoneStale(stale, $"Allowed in {nameof(StepWordingTests)} but no longer found (remove)");
    }

    private static string Describe((string File, string Literal) phrase) => $"{phrase.File}: {phrase.Literal}";
}
