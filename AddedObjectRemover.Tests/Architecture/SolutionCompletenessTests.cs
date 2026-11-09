namespace AddedObjectRemover.Tests.Architecture;

public class SolutionCompletenessTests
{
    private const string ProjectFileExtension = ".csproj";

    private const string OutputTypeProperty = "OutputType";

    private const string ExeOutputType = "Exe";

    private static readonly string[] SharedWarningProperties =
        ["NoWarn", "Nullable", "WarningsAsErrors", "WarningsNotAsErrors", "TreatWarningsAsErrors", "EnforceCodeStyleInBuild"];

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
            Assert.Equal("Library", SolutionLayout.Property(project, OutputTypeProperty));
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

    /// <summary>The warning and nullable settings come only from Directory.Build.props, so every project is judged by the same ones.</summary>
    [Fact]
    public void NoProjectFileChangesTheSharedWarningSettings()
    {
        var offenders = SolutionLayout.ProjectFiles()
            .SelectMany(file => SharedWarningProperties
                .Where(property => SolutionLayout.Property(SolutionLayout.Load(file), property) is not null)
                .Select(property => $"{SolutionLayout.ProjectName(file)}: {property}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void OnlyTheEntryProjectIsAnExe()
    {
        var entryProjects = Rules.Projects.Where(project => project.Kind == ProjectRules.EntryKind).Select(project => project.Name).Order(StringComparer.Ordinal).ToList();
        var exeProjects = SolutionLayout.ProjectFiles()
            .Where(file => SolutionLayout.Property(SolutionLayout.Load(file), OutputTypeProperty) == ExeOutputType)
            .Select(SolutionLayout.ProjectName)
            .Order(StringComparer.Ordinal);

        Assert.NotEmpty(entryProjects);
        Assert.Equal(entryProjects, exeProjects);
    }

    private static string ProjectFileOf(ProjectRule rule) =>
        Path.Combine(SolutionLayout.Root(), rule.Folder, rule.Name + ProjectFileExtension);
}
