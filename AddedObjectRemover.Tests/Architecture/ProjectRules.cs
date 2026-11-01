using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// One row of the project table: where the project is, what kind it is, which projects it may reference
/// and which packages ("name version") it may use.
/// </summary>
internal sealed record ProjectRule(string Name, string Kind, string Folder, IReadOnlyList<string> References, IReadOnlyList<string> Packages);

/// <summary>
/// The project table from ProjectRules.json: one row per project, listing exactly the projects its csproj references.
/// Kinds: K shared ids, A and T tools, C contracts, Q cache catalog, E entry, X test, R runner, L logic, B cache builder,
/// V settings validation.
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
    private const string StepsFolderPrefix = "Steps/";
    private const string CachesFolderPrefix = "Caches/";
    private const string RunFolderPrefix = "Run/";
    private const string RunCachesContractsName = "RunCaches.Contracts";

    public static readonly IReadOnlyList<string> LogicKinds = ["L", "B"];

    /// <summary>The logic projects that may reference the run's contracts, with the reason.</summary>
    public static readonly IReadOnlyDictionary<string, string> LogicReferencesToRunContracts = new Dictionary<string, string>
    {
        ["BuildLogAndReports -> RunSettingsValidation.Contracts"] = "the log prints the run's settings and the batch request, which these contracts define",
    };

    public static readonly IReadOnlyList<string> ToolKinds = ["A", "T"];

    private static readonly IReadOnlyList<string> ContractsKinds = [ContractsKind, CacheCatalogKind];

    public static ProjectRules Load([CallerFilePath] string sourceFile = "") =>
        JsonSerializer.Deserialize<ProjectRules>(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(sourceFile)!, "ProjectRules.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public ProjectRule Find(string name) => Projects.Single(project => project.Name == name);

    public bool IsLogic(ProjectRule project) => LogicKinds.Contains(project.Kind);

    public bool IsTool(ProjectRule project) => ToolKinds.Contains(project.Kind);

    public bool IsContracts(ProjectRule project) => ContractsKinds.Contains(project.Kind);

    private static bool IsIds(ProjectRule project) => project.Kind == IdsKind;

    private static bool IsStepContracts(ProjectRule project) =>
        project.Kind == ContractsKind && InFolder(project, StepsFolderPrefix);

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

    public IReadOnlyList<string> FindToolsReferencingNonTools() =>
        FindReferences(IsTool, project => !IsTool(project) && !IsIds(project));

    public IReadOnlyList<string> FindContractsReferencingNonContractsAndNonTools() =>
        FindReferences(IsContracts, project => !IsContracts(project) && !IsTool(project) && !IsIds(project));

    /// <summary>Cache contracts sit between the steps and the caches: they may see step contracts and other cache contracts, but not the run's own contracts.</summary>
    public IReadOnlyList<string> FindCacheContractsReferencingRunContracts() =>
        FindReferences(IsCacheContracts, IsRunContracts);

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
