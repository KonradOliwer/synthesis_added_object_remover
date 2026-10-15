namespace AddedObjectRemover.Tests.Architecture;

public class PackageRulesTests
{
    private static readonly ProjectRules Rules = ProjectRules.Load();

    /// <summary>Kinds that must not see the plugin, mesh, geometry-library or settings-file packages: commons, package-free tool API, contracts, cache catalog, logic, cache builders.</summary>
    private static readonly string[] PackageFreeKinds = ["K", "A", "C", "Q", "L", "B"];

    private static readonly string[] ForbiddenPackagePrefixes = ["Mutagen", "Nifly", "Newtonsoft", "NetTopologySuite"];

    /// <summary>The one project that may compile against NetTopologySuite among the package-free kinds (design 10.4).</summary>
    private const string NetTopologySuiteUser = "MoveKeptMarkers";

    [Fact]
    public void EachProjectHoldsOnlyThePackagesItsRowGives()
    {
        var breaches = SolutionLayout.ProjectFiles().SelectMany(FindUnlistedPackages).ToList();

        Assert.Empty(breaches);
    }

    [Fact]
    public void PackageFreeProjectsCompileAgainstNoPluginMeshGeometryOrSettingsFilePackage()
    {
        var checkedProjects = SolutionLayout.ProjectFiles().Where(IsPackageFree).ToList();

        var breaches = checkedProjects.SelectMany(FindForbiddenCompiledPackages).ToList();

        Assert.NotEmpty(checkedProjects);
        Assert.Empty(breaches);
    }

    [Fact]
    public void TheCompileSetReaderSeesThePackagesOfTheEntry()
    {
        Assert.Contains("Mutagen.Bethesda.Skyrim", ProjectPackages.CompiledAgainst(EntryProjectFile()));
    }

    [Fact]
    public void TheEntryListsTheSameMutagenVersionAsThePluginTools()
    {
        var toolsFile = SolutionLayout.ProjectFiles()
            .Single(file => SolutionLayout.ProjectName(file) == "PluginRecordReadingAndWriting");

        Assert.Equal(MutagenSkyrimPackage(toolsFile), MutagenSkyrimPackage(EntryProjectFile()));
    }

    private static string MutagenSkyrimPackage(string projectFile) =>
        ProjectPackages.Declared(projectFile).Single(package => package.StartsWith("Mutagen.Bethesda.Skyrim ", StringComparison.Ordinal));

    private static IEnumerable<string> FindUnlistedPackages(string projectFile)
    {
        var name = SolutionLayout.ProjectName(projectFile);
        return ProjectPackages.Declared(projectFile)
            .Except(Rules.Find(name).Packages)
            .Select(package => $"{name} holds {package}, which the table gives to {OwnerOf(package)}");
    }

    private static string OwnerOf(string package) =>
        Rules.Projects.FirstOrDefault(row => row.Packages.Contains(package))?.Name ?? "no project";

    private static bool IsPackageFree(string projectFile) =>
        PackageFreeKinds.Contains(Rules.Find(SolutionLayout.ProjectName(projectFile)).Kind);

    private static IEnumerable<string> FindForbiddenCompiledPackages(string projectFile)
    {
        var name = SolutionLayout.ProjectName(projectFile);
        return ProjectPackages.CompiledAgainst(projectFile)
            .Where(package => ForbiddenPackagePrefixes.Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Where(package => !(name == NetTopologySuiteUser && package.StartsWith("NetTopologySuite", StringComparison.OrdinalIgnoreCase)))
            .Select(package => $"{name} compiles against {package}");
    }

    private static string EntryProjectFile() =>
        SolutionLayout.ProjectFiles().Single(file => Rules.Find(SolutionLayout.ProjectName(file)).Kind == ProjectRules.EntryKind);
}
