namespace AddedObjectRemover.Tests.Architecture;

internal sealed record ProjectSources(string Project, string Folder, IReadOnlyList<string> Files);

/// <summary>The source files of each production project, for tests that guard where certain code may appear.</summary>
internal static class ProductionSources
{
    private static readonly Lazy<IReadOnlyList<ProjectSources>> AllProjects = new(FindProjects);

    /// <exception cref="InvalidOperationException">A project has no source files, so a scan of it would pass by finding nothing.</exception>
    public static IReadOnlyList<ProjectSources> Projects() => AllProjects.Value;

    /// <summary>The built assembly of every production project (the assembly name is the project name).</summary>
    public static IReadOnlyList<System.Reflection.Assembly> Assemblies() =>
        Projects().Select(project => System.Reflection.Assembly.Load(project.Project)).ToList();

    private static IReadOnlyList<ProjectSources> FindProjects() =>
        SolutionLayout.ProjectFiles()
            .Where(projectFile => !SolutionLayout.IsTestProject(projectFile))
            .Select(SourcesOf)
            .ToList()
            .AsReadOnly();

    private static ProjectSources SourcesOf(string projectFile)
    {
        var projectFolder = Path.GetDirectoryName(projectFile)!;
        var files = SolutionLayout.FilesBelow(projectFolder, "*.cs");
        if (files.Count == 0) throw new InvalidOperationException($"No source files found for {projectFile}; the scan would pass without checking anything.");
        return new ProjectSources(SolutionLayout.ProjectName(projectFile), projectFolder, files);
    }
}
