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

    private static IEnumerable<string> FindRowBreaches(string name, string folder, IReadOnlyList<string> references)
    {
        var row = Rules.Projects.SingleOrDefault(rule => rule.Name == name);
        if (row is null)
        {
            yield return $"{name} has no row in ProjectRules.json";
            yield break;
        }
        if (row.Folder != folder) yield return $"{name} is in {folder}, the table says {row.Folder}";
        foreach (var reference in references.Except(row.References)) yield return $"{name} references {reference}, which the table does not list";
        foreach (var reference in row.References.Except(references)) yield return $"{name} has no reference to {reference}, which the table lists";
    }

    /// <summary>The table rows of the projects in the solution, each with the references its csproj really has.</summary>
    private static ProjectRules RulesWithActualReferences(IReadOnlyList<(string Name, string Folder, IReadOnlyList<string> References)> projects)
    {
        var rows = projects
            .Where(project => HasRow(project.Name))
            .Select(project => Rules.Find(project.Name) with { References = project.References.Where(HasRow).ToList() })
            .ToList();
        return new ProjectRules(Rules.EntryOnlyProjects, rows);
    }

    private static bool HasRow(string name) => Rules.Projects.Any(rule => rule.Name == name);

    private static IEnumerable<string> FindKindBreaches(ProjectRules actual) =>
        actual.FindLogicReferencingLogic().Select(reference => $"{reference}: logic references logic")
            .Concat(actual.FindLogicReferencedByNonRunners().Select(reference => $"{reference}: logic referenced by a non-runner"))
            .Concat(actual.FindEntryOnlyReferencedByOthers().Select(reference => $"{reference}: entry-only project referenced by another project"))
            .Concat(actual.FindStepContractsReferencingRunCachesContracts().Select(reference => $"{reference}: step contracts reference the run caches"))
            .Concat(actual.FindStepContractsReferencingRunContracts().Select(reference => $"{reference}: step contracts reference the run's contracts"))
            .Concat(actual.FindLogicReferencingRunContractsWithoutReason().Select(reference => $"{reference}: logic references the run's contracts without a listed reason"))
            .Concat(actual.FindToolsReferencingNonTools().Select(reference => $"{reference}: tool references something other than tools and ids"))
            .Concat(actual.FindContractsReferencingNonContractsAndNonTools().Select(reference => $"{reference}: contracts reference something other than contracts, tools and ids"))
            .Concat(actual.FindCacheContractsReferencingRunContracts().Select(reference => $"{reference}: cache contracts reference the run's contracts"))
            .Concat(actual.FindCycles().Select(cycle => $"{cycle}: reference cycle"));
}
