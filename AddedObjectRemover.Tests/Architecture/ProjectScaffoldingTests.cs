namespace AddedObjectRemover.Tests.Architecture;

public class ProjectScaffoldingTests
{
    private static readonly ProjectRules Rules = ProjectRules.Load();

    [Fact]
    public void EveryProjectInTheSolutionHasARowAndObeysIt()
    {
        var projects = SolutionLayout.SolutionProjectFiles()
            .Select(file => (Name: SolutionLayout.ProjectName(file), Folder: SolutionLayout.FolderRelativeToRoot(file), References: SolutionLayout.ReferencedProjectNames(file)))
            .ToList();

        var rowBreaches = projects.SelectMany(project => FindRowBreaches(project.Name, project.Folder, project.References)).ToList();
        var kindBreaches = FindKindBreaches(RulesWithActualReferences(projects)).ToList();

        Assert.Empty(rowBreaches.Concat(kindBreaches));
    }

    private const string EntryProject = "AddedObjectRemover";

    /// <summary>
    /// Temporary: while the entry project still holds the code of the projects not yet split out, it references the
    /// tools directly. Steps 58-79 delete names from this list; it must be empty when the split is done.
    /// </summary>
    private static readonly string[] EntryToolReferencesUntilTheSplitIsDone =
    [
        "BoxAndTransformMath", "OrderIndependentParallelWork", "KeyedGroupingInInputOrder", "DenseIdTables",
        "IndexGraphQueries", "NumericRanges", "FileSystemAccess", "ComputedOnceLookups",
        "TriangleMeshTests", "BoxSpatialIndex", "GroundSurfaceQueries", "LogTextFormatting", "CsvFileWriting",
    ];

    private static IEnumerable<string> FindRowBreaches(string name, string folder, IReadOnlyList<string> references)
    {
        var row = Rules.Projects.SingleOrDefault(rule => rule.Name == name);
        if (row is null)
        {
            yield return $"{name} has no row in ProjectRules.json";
            yield break;
        }
        if (row.Folder != folder) yield return $"{name} is in {folder}, the table says {row.Folder}";
        var allowed = name == EntryProject ? row.References.Concat(EntryToolReferencesUntilTheSplitIsDone) : row.References;
        foreach (var reference in references.Except(allowed)) yield return $"{name} references {reference}, which the table does not allow";
        if (name != EntryProject) yield break;
        foreach (var unused in EntryToolReferencesUntilTheSplitIsDone.Except(references)) yield return $"{name} no longer references {unused}: remove it from the temporary list";
    }

    /// <summary>The table restricted to the projects that exist, each with the references its csproj really has.</summary>
    private static ProjectRules RulesWithActualReferences(IReadOnlyList<(string Name, string Folder, IReadOnlyList<string> References)> projects)
    {
        var existing = projects.Select(project => project.Name).Where(name => Rules.Projects.Any(rule => rule.Name == name)).ToList();
        var rows = projects
            .Where(project => existing.Contains(project.Name))
            .Select(project => Rules.Find(project.Name) with { References = project.References.Where(existing.Contains).ToList() })
            .ToList();
        return new ProjectRules(Rules.EntryOnlyProjects, rows);
    }

    private static IEnumerable<string> FindKindBreaches(ProjectRules actual) =>
        actual.FindLogicReferencingLogic().Select(reference => $"{reference}: logic references logic")
            .Concat(actual.FindLogicReferencedByNonRunners().Select(reference => $"{reference}: logic referenced by a non-runner"))
            .Concat(actual.FindEntryOnlyReferencedByOthers().Select(reference => $"{reference}: entry-only project referenced by another project"))
            .Concat(actual.FindCycles().Select(cycle => $"{cycle}: reference cycle"));
}
