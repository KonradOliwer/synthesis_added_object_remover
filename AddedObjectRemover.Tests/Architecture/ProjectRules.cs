using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>One row of ProjectRules.json: what kind the project is and which packages ("name version") it may use.</summary>
internal sealed record ProjectRow(string Name, string Kind, IReadOnlyList<string> Packages);

/// <summary>ProjectRules.json as written: the projects only the entry may reference, and one row per project.</summary>
internal sealed record ProjectTable(IReadOnlyList<string> EntryOnlyProjects, IReadOnlyList<ProjectRow> Projects)
{
    public static ProjectTable Load([CallerFilePath] string sourceFile = "") =>
        JsonSerializer.Deserialize<ProjectTable>(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(sourceFile)!, "ProjectRules.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}

/// <summary>
/// A project's row with where the project is and which projects its csproj references, so the kind rules judge the
/// real references.
/// </summary>
internal sealed record ProjectRule(string Name, string Kind, string Folder, IReadOnlyList<string> References, IReadOnlyList<string> Packages);

/// <summary>
/// The rows of ProjectRules.json whose project is in the solution, each with the folder and the references (to projects
/// with a row) its csproj has. Kinds: K shared ids, A and T tools, C contracts, Q cache catalog, E entry, X test,
/// R runner, L step logic, U rule logic, B cache builder, V settings validation.
/// </summary>
internal sealed record ProjectRules(IReadOnlyList<string> EntryOnlyProjects, IReadOnlyList<ProjectRule> Projects)
{
    public const string EntryKind = "E";
    public const string TestKind = "X";
    public const string RunnerKind = "R";
    public const string ValidationKind = "V";
    public const string IdsKind = "K";

    private const string ContractsKind = "C";
    private const string CacheCatalogKind = "Q";
    private const string StepLogicKind = "L";
    private const string CacheBuilderKind = "B";
    private const string RuleLogicKind = "U";
    private const string IdsFolder = "Ids";
    private const string ToolsFolderPrefix = "Tools/";
    private const string StepsFolderPrefix = "Steps/";
    private const string RulesFolderPrefix = "Rules/";
    private const string CachesFolderPrefix = "Caches/";
    private const string RunFolderPrefix = "Run/";
    private const string ContractsNameSuffix = ".Contracts";
    private const string TestsNameSuffix = ".Tests";
    private const string RunCachesContractsName = "RunCaches.Contracts";

    public static readonly IReadOnlyList<string> LogicKinds = [StepLogicKind, CacheBuilderKind, RuleLogicKind];

    /// <summary>The logic projects that may reference the run's contracts, with the reason.</summary>
    public static readonly IReadOnlyDictionary<string, string> LogicReferencesToRunContracts = new Dictionary<string, string>
    {
        ["BuildLogAndReports -> RunSettingsValidation.Contracts"] = "the log prints the run's settings and the batch request, which these contracts define",
    };

    /// <summary>The step contracts that describe the world a rule judges, which every rule project may reference, with the reason.</summary>
    public static readonly IReadOnlyDictionary<string, string> RuleWorldDataReferences = new Dictionary<string, string>
    {
        ["CollectPlacedObjects.Contracts"] = "the placed objects, record versions and restored parts a rule judges",
        ["RemovalDecisionList.Contracts"] = "the decisions the follow-up rule works on, and the removal reasons a finding is worded as",
        ["IdentifyTheMods.Contracts"] = "the trust table the other-mods support rules read",
        ["FindTargetObjectsToKeep.Contracts"] = "the keep reasons the decisions carry, among them the held-up targets the other-mods support rule finds",
    };

    /// <summary>The step projects that may reference another step's contracts, with the result of that step they read.</summary>
    public static readonly IReadOnlyDictionary<string, string> StepResultReferences = new[]
    {
        Reads("the placed objects the scan collected", "CollectPlacedObjects.Contracts",
            "SelectObjectsThatCanCauseRemovals", "FindTargetObjectsToKeep",
            "RemovalDecisionList.Contracts", "RemovalDecisionList", "JudgeChangedObjects.Contracts", "JudgeChangedObjects",
            "FitClashingObjects", "RemoveObjectsOnChangedTerrain.Contracts", "RemoveObjectsOnChangedTerrain",
            "RemoveTooCloseObjects.Contracts", "RemoveTooCloseObjects", "RemoveObjectsRestingOnRemovedOnes",
            "RemoveVisibleObjectsLeftBehind.Contracts", "RemoveVisibleObjectsLeftBehind", "RemoveObjectsInClearedAreas",
            "RestoreWhereRemoved.Contracts", "RestoreWhereRemoved", "RemoveInvisibleObjectsLeftBehind.Contracts",
            "RemoveInvisibleObjectsLeftBehind", "MoveKeptMarkers.Contracts", "MoveKeptMarkers", "WriteThePatch"),
        Reads("the trust table of the identified mods", "IdentifyTheMods.Contracts", "CollectPlacedObjects"),
        Reads("the targets the keep step keeps", "FindTargetObjectsToKeep.Contracts",
            "RemovalDecisionList.Contracts", "RemovalDecisionList", "FitClashingObjects", "RemoveVisibleObjectsLeftBehind.Contracts",
            "RemoveVisibleObjectsLeftBehind", "RemoveObjectsInClearedAreas.Contracts", "RemoveInvisibleObjectsLeftBehind.Contracts"),
        Reads("the run's removal decisions", "RemovalDecisionList.Contracts",
            "JudgeChangedObjects.Contracts", "JudgeChangedObjects", "FitClashingObjects.Contracts", "FitClashingObjects",
            "RemoveObjectsOnChangedTerrain.Contracts", "RemoveObjectsOnChangedTerrain", "RemoveTooCloseObjects.Contracts",
            "RemoveTooCloseObjects", "RemoveObjectsRestingOnRemovedOnes", "OtherModsObjectsLeftWithoutSupport", "RemoveVisibleObjectsLeftBehind.Contracts",
            "RemoveVisibleObjectsLeftBehind", "RemoveObjectsInClearedAreas.Contracts", "RemoveObjectsInClearedAreas", "RestoreWhereRemoved",
            "RemoveInvisibleObjectsLeftBehind.Contracts", "RemoveInvisibleObjectsLeftBehind", "MoveKeptMarkers.Contracts",
            "MoveKeptMarkers", "WriteThePatch"),
        Reads("the left-behind toolkit and the visible objects left behind", "RemoveVisibleObjectsLeftBehind.Contracts",
            "RemoveObjectsInClearedAreas.Contracts", "RemoveObjectsInClearedAreas", "RestoreWhereRemoved",
            "RemoveInvisibleObjectsLeftBehind.Contracts", "RemoveInvisibleObjectsLeftBehind"),
        Reads("the cleared areas", "RemoveObjectsInClearedAreas.Contracts", "RestoreWhereRemoved.Contracts", "RestoreWhereRemoved"),
        Reads("the restore's result it writes", "RestoreWhereRemoved.Contracts", "WriteThePatch"),
        Reads("the invisible objects left behind whose markers move", "RemoveInvisibleObjectsLeftBehind.Contracts", "MoveKeptMarkers.Contracts", "MoveKeptMarkers"),
        Reads("the step results it logs and reports",
            ["CollectPlacedObjects.Contracts", "FindTargetObjectsToKeep.Contracts", "FitClashingObjects.Contracts", "MoveKeptMarkers.Contracts", "RemovalDecisionList.Contracts",
             "RemoveInvisibleObjectsLeftBehind.Contracts", "RemoveObjectsInClearedAreas.Contracts", "RemoveObjectsOnChangedTerrain.Contracts",
             "RemoveTooCloseObjects.Contracts", "RemoveVisibleObjectsLeftBehind.Contracts",
             "RestoreWhereRemoved.Contracts", "SelectObjectsThatCanCauseRemovals.Contracts", "WriteThePatch.Contracts"],
            "BuildLogAndReports.Contracts", "BuildLogAndReports"),
        Reads("the step results its report contracts describe", "JudgeChangedObjects.Contracts", "BuildLogAndReports.Contracts"),
        Reads("the trust table it logs", "IdentifyTheMods.Contracts", "BuildLogAndReports"),
    }.SelectMany(entries => entries).ToDictionary(entry => entry.Key, entry => entry.Value);

    private static IEnumerable<KeyValuePair<string, string>> Reads(string reason, string stepContracts, params string[] readers) =>
        Reads(reason, [stepContracts], readers);

    private static IEnumerable<KeyValuePair<string, string>> Reads(string reason, IReadOnlyList<string> stepContracts, params string[] readers) =>
        readers.SelectMany(reader => stepContracts.Select(contracts => KeyValuePair.Create($"{reader} -> {contracts}", reason)));

    public static readonly IReadOnlyList<string> ToolKinds = ["A", "T"];

    private static readonly IReadOnlyList<string> ContractsKinds = [ContractsKind, CacheCatalogKind];

    public static ProjectRules Load()
    {
        var table = ProjectTable.Load();
        var projectFiles = SolutionLayout.SolutionProjectFiles().ToDictionary(SolutionLayout.ProjectName);
        var rows = table.Projects.Where(row => projectFiles.ContainsKey(row.Name)).ToList();
        var names = rows.Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        return new ProjectRules(table.EntryOnlyProjects, [.. rows.Select(row => ToRule(row, projectFiles[row.Name], names))]);
    }

    private static ProjectRule ToRule(ProjectRow row, string projectFile, IReadOnlySet<string> names) =>
        new(
            row.Name,
            row.Kind,
            SolutionLayout.FolderRelativeToRoot(projectFile),
            [.. SolutionLayout.ReferencedProjectNames(projectFile).Where(names.Contains)],
            row.Packages);

    public ProjectRule Find(string name) => Projects.Single(project => project.Name == name);

    public bool IsLogic(ProjectRule project) => LogicKinds.Contains(project.Kind);

    public bool IsRuleLogic(ProjectRule project) => project.Kind == RuleLogicKind;

    public bool IsTool(ProjectRule project) => ToolKinds.Contains(project.Kind);

    public bool IsContracts(ProjectRule project) => ContractsKinds.Contains(project.Kind);

    private static bool IsIds(ProjectRule project) => project.Kind == IdsKind;

    private static bool IsStepContracts(ProjectRule project) =>
        project.Kind == ContractsKind && InFolder(project, StepsFolderPrefix);

    private static bool IsStep(ProjectRule project) => InFolder(project, StepsFolderPrefix);

    private static bool IsRule(ProjectRule project) => InFolder(project, RulesFolderPrefix);

    private static bool IsRuleContracts(ProjectRule project) => project.Kind == ContractsKind && IsRule(project);

    private static string StepName(string projectName) =>
        projectName.EndsWith(ContractsNameSuffix, StringComparison.Ordinal) ? projectName[..^ContractsNameSuffix.Length] : projectName;

    private bool IsCacheContracts(ProjectRule project) => IsContracts(project) && InFolder(project, CachesFolderPrefix);

    private bool IsRunContracts(ProjectRule project) => IsContracts(project) && InFolder(project, RunFolderPrefix);

    private static bool InFolder(ProjectRule project, string folderPrefix) =>
        project.Folder.StartsWith(folderPrefix, StringComparison.Ordinal);

    /// <summary>Projects that only the entry and the tests may reference.</summary>
    public bool IsEntryOnly(ProjectRule project) => project.Kind == ValidationKind || EntryOnlyProjects.Contains(project.Name);

    public IReadOnlyList<string> FindLogicReferencingLogic() => FindReferences(IsLogic, IsLogic);

    public IReadOnlyList<string> FindLogicReferencedByNonRunners() =>
        FindReferences(project => project.Kind != RunnerKind && project.Kind != TestKind, IsLogic);

    public IReadOnlyList<string> FindEntryOnlyReferencedByOthers() =>
        FindReferences(project => project.Kind != EntryKind && project.Kind != TestKind, IsEntryOnly);

    public IReadOnlyList<string> FindStepContractsReferencingRunCachesContracts() =>
        FindReferences(IsStepContracts, project => project.Name == RunCachesContractsName);

    public IReadOnlyList<string> FindStepContractsReferencingRunContracts() =>
        FindReferences(IsStepContracts, IsRunContracts);

    public IReadOnlyList<string> FindLogicReferencingRunContractsWithoutReason() =>
        FindReferences(IsLogic, IsRunContracts).Except(LogicReferencesToRunContracts.Keys).ToList();

    public IReadOnlyList<string> FindListedLogicReferencesToRunContractsThatDoNotExist() =>
        LogicReferencesToRunContracts.Keys.Except(FindReferences(IsLogic, IsRunContracts)).ToList();

    public IReadOnlyList<string> FindRulesReferencingOtherThanToolsIdsCachesRulesAndTheWorldData() =>
        FindReferences(IsRule, project => !IsTool(project) && !IsIds(project) && !IsCacheContracts(project) && !IsRuleContracts(project) && !RuleWorldDataReferences.ContainsKey(project.Name));

    public IReadOnlyList<string> FindStepsReferencingOtherStepsWithoutReason() =>
        FindStepReferencesToOtherStepsContracts().Except(StepResultReferences.Keys).ToList();

    public IReadOnlyList<string> FindListedStepResultReferencesThatDoNotExist() =>
        StepResultReferences.Keys.Except(FindStepReferencesToOtherStepsContracts()).ToList();

    private List<string> FindStepReferencesToOtherStepsContracts() =>
        Projects
            .Where(IsStep)
            .SelectMany(project => project.References
                .Where(reference => IsStepContracts(Find(reference)) && StepName(reference) != StepName(project.Name))
                .Select(reference => $"{project.Name} -> {reference}"))
            .ToList();

    public IReadOnlyList<string> FindToolsReferencingNonTools() =>
        FindReferences(IsTool, project => !IsTool(project) && !IsIds(project));

    public IReadOnlyList<string> FindContractsReferencingNonContractsAndNonTools() =>
        FindReferences(IsContracts, project => !IsContracts(project) && !IsTool(project) && !IsIds(project));

    /// <summary>Cache contracts sit between the steps and the caches: they may see step contracts and other cache contracts, but not the run's own contracts.</summary>
    public IReadOnlyList<string> FindCacheContractsReferencingRunContracts() =>
        FindReferences(IsCacheContracts, IsRunContracts);

    /// <summary>One line per row whose kind is none of those its folder and name allow, so a logic project marked as contracts cannot pass every kind rule.</summary>
    public IReadOnlyList<string> FindRowsWhoseKindDoesNotFitFolderAndName() =>
        Projects
            .Where(project => !KindsThatFit(project).Contains(project.Kind))
            .Select(project => $"{project.Name} ({project.Folder}) is kind {project.Kind}; its folder and name allow {DescribeKinds(KindsThatFit(project))}")
            .ToList();

    /// <summary>
    /// Ids: K. Tools: T or A. Under Steps: C for a contracts project, otherwise L. Under Rules: C for a contracts project, otherwise U.
    /// Under Caches: C or Q for a contracts project, otherwise B.
    /// Under Run: C for a contracts project, otherwise V or R. Outside those folders: X for the tests, otherwise E for the entry.
    /// </summary>
    private static IReadOnlyList<string> KindsThatFit(ProjectRule project)
    {
        var isContracts = project.Name.EndsWith(ContractsNameSuffix, StringComparison.Ordinal);
        if (project.Folder == IdsFolder) return [IdsKind];
        if (InFolder(project, ToolsFolderPrefix)) return ToolKinds;
        if (InFolder(project, StepsFolderPrefix)) return isContracts ? [ContractsKind] : [StepLogicKind];
        if (InFolder(project, RulesFolderPrefix)) return isContracts ? [ContractsKind] : [RuleLogicKind];
        if (InFolder(project, CachesFolderPrefix)) return isContracts ? ContractsKinds : [CacheBuilderKind];
        if (InFolder(project, RunFolderPrefix)) return isContracts ? [ContractsKind] : [ValidationKind, RunnerKind];
        if (project.Folder.Contains('/')) return [];
        return project.Name.EndsWith(TestsNameSuffix, StringComparison.Ordinal) ? [TestKind] : [EntryKind];
    }

    private static string DescribeKinds(IReadOnlyList<string> kinds) =>
        kinds.Count == 0 ? "no kind" : $"only {string.Join(" or ", kinds)}";

    /// <summary>One line per reference that closes a loop, written as the loop's path, e.g. "A -> B -> A".</summary>
    public IReadOnlyList<string> FindCycles()
    {
        var cycles = new List<string>();
        var done = new HashSet<string>();
        foreach (var project in Projects) CollectCyclesFrom(project.Name, [], done, cycles);
        return cycles;
    }

    private List<string> FindReferences(Func<ProjectRule, bool> fromProject, Func<ProjectRule, bool> toProject) =>
        Projects
            .Where(fromProject)
            .SelectMany(project => project.References.Where(reference => toProject(Find(reference))).Select(reference => $"{project.Name} -> {reference}"))
            .ToList();

    private void CollectCyclesFrom(string name, List<string> path, HashSet<string> done, List<string> cycles)
    {
        if (path.Contains(name))
        {
            cycles.Add(string.Join(" -> ", path.SkipWhile(step => step != name).Append(name)));
            return;
        }
        if (done.Contains(name)) return;
        path.Add(name);
        foreach (var reference in Find(name).References) CollectCyclesFrom(reference, path, done, cycles);
        path.RemoveAt(path.Count - 1);
        done.Add(name);
    }
}
