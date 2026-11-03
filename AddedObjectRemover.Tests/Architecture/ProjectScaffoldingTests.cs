namespace AddedObjectRemover.Tests.Architecture;

public class ProjectScaffoldingTests
{
    [Fact]
    public void EveryProjectInTheSolutionHasARowAndEveryRowAProject()
    {
        var projectNames = SolutionLayout.SolutionProjectFiles().Select(SolutionLayout.ProjectName).ToList();
        var rowNames = ProjectTable.Load().Projects.Select(row => row.Name).ToList();

        Assert.Empty(
            projectNames.Except(rowNames).Select(name => $"{name} has no row in ProjectRules.json")
                .Concat(rowNames.Except(projectNames).Select(name => $"{name} has a row in ProjectRules.json but is not in the solution")));
    }

    [Fact]
    public void TheTableHasNoDuplicateNames()
    {
        var names = ProjectTable.Load().Projects.Select(row => row.Name).ToList();

        Assert.Equal(names.Distinct(), names);
    }
}
