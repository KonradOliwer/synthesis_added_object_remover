using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// One row of the project table: where the project is, what kind it is, which projects it may reference
/// and which packages ("name version") it may use. The row may list projects that do not exist yet.
/// </summary>
internal sealed record ProjectRule(string Name, string Kind, string Folder, IReadOnlyList<string> References, IReadOnlyList<string> Packages);

/// <summary>
/// The project table from ProjectRules.json: one row per project of the finished layout, so a row can exist before its
/// project does. Kinds: E entry, X test, R runner, L logic, B cache builder,
/// V settings validation; the other kinds (contracts, tools, cache catalog) have no rule of their own.
/// </summary>
internal sealed record ProjectRules(IReadOnlyList<string> EntryOnlyProjects, IReadOnlyList<ProjectRule> Projects)
{
    public const string EntryKind = "E";
    public const string TestKind = "X";
    public const string RunnerKind = "R";
    public const string ValidationKind = "V";

    public static readonly IReadOnlyList<string> LogicKinds = ["L", "B"];

    public static ProjectRules Load([CallerFilePath] string sourceFile = "") =>
        JsonSerializer.Deserialize<ProjectRules>(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(sourceFile)!, "ProjectRules.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public ProjectRule Find(string name) => Projects.Single(project => project.Name == name);

    public bool IsLogic(ProjectRule project) => LogicKinds.Contains(project.Kind);

    /// <summary>Projects that only the entry and the tests may reference.</summary>
    public bool IsEntryOnly(ProjectRule project) => project.Kind == ValidationKind || EntryOnlyProjects.Contains(project.Name);

    public IReadOnlyList<string> FindLogicReferencingLogic() => FindReferences(IsLogic, IsLogic);

    public IReadOnlyList<string> FindLogicReferencedByNonRunners() =>
        FindReferences(project => project.Kind != RunnerKind && project.Kind != TestKind, IsLogic);

    public IReadOnlyList<string> FindEntryOnlyReferencedByOthers() =>
        FindReferences(project => project.Kind != EntryKind && project.Kind != TestKind, IsEntryOnly);

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
