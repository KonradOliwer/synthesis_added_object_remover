namespace AddedObjectRemover.Tests.Architecture;

public class SolutionCompletenessTests
{
    private static readonly ProjectRules Rules = ProjectRules.Load();

    [Fact]
    public void TheSolutionListsEveryProject() =>
        Assert.Equal(SolutionLayout.ProjectFiles(), SolutionLayout.SolutionProjectFiles());

    [Fact]
    public void EveryProjectHasATargetFrameworkAndAnItemGroup()
    {
        var offenders = SolutionLayout.ProjectFiles()
            .Select(file => (Name: SolutionLayout.ProjectName(file), Project: SolutionLayout.Load(file)))
            .Where(project => string.IsNullOrEmpty(SolutionLayout.Property(project.Project, "TargetFramework")) || !project.Project.Descendants("ItemGroup").Any())
            .Select(project => project.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheTestProjectsAreUnpackedTestLibraries()
    {
        var testProjects = Rules.Projects.Where(project => project.Kind == ProjectRules.TestKind).ToList();

        Assert.NotEmpty(testProjects);
        foreach (var rule in testProjects)
        {
            var project = SolutionLayout.Load(ProjectFileOf(rule));
            Assert.Equal("Library", SolutionLayout.Property(project, "OutputType"));
            Assert.Equal("false", SolutionLayout.Property(project, "IsPackable"));
            Assert.Equal("true", SolutionLayout.Property(project, "IsTestProject"));
        }
    }

    [Fact]
    public void NoProjectIsInsideAnotherProjectsFolder()
    {
        var folders = SolutionLayout.ProjectFiles().Select(SolutionLayout.FolderRelativeToRoot).ToList();
        var nested = folders
            .SelectMany(inner => folders.Where(outer => inner != outer && inner.StartsWith(outer + "/", StringComparison.Ordinal)).Select(outer => $"{inner} is inside {outer}"))
            .ToList();

        Assert.Empty(nested);
    }

    private static string ProjectFileOf(ProjectRule rule) =>
        SolutionLayout.ProjectFiles().Single(file => SolutionLayout.ProjectName(file) == rule.Name);
}
