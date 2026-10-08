namespace AddedObjectRemover.Tests.Architecture;

public class PackageRulesTests
{
    private static readonly ProjectRules Rules = ProjectRules.Load();

    /// <summary>Kinds that must not see the plugin, mesh, geometry-library or settings-file packages: commons, package-free tool API, contracts, cache catalog, logic, cache builders.</summary>
    private static readonly string[] PackageFreeKinds = ["K", "A", "C", "Q", "L", "B"];

    private static readonly string[] ForbiddenPackagePrefixes = ["Mutagen", "Nifly", "Newtonsoft", "NetTopologySuite"];

    /// <summary>The one project that may compile against NetTopologySuite among the package-free kinds (design 10.4).</summary>
    private const string NetTopologySuiteUser = "MoveKeptMarkers";

    /// <summary>
    /// TEMPORARY. Until the tool, validation and runner projects are split out (steps 58-79), the entry project still
    /// holds the packages of the projects that do not exist yet. Each step that creates one of those projects must delete
    /// the entry's package and its line here; the tests below fail while a listed package's project already exists,
    /// and when a listed package is no longer in the entry. The list must end up empty.
    /// </summary>
    private static readonly string[] EntryPackagesUntilTheirProjectsExist =
    [
        "Mutagen.Bethesda 0.54.2",           // direct reference with no row of its own; row 3 reaches it through Mutagen.Bethesda.Skyrim
        "Mutagen.Bethesda.Skyrim 0.54.2",    // row 3, PluginRecordReadingAndWriting
        "Nifly [1.1.0]",                     // row 5, MeshFileReading
        "SharpZipLib 1.4.2",                 // row 5
        "ini-parser-netstandard 2.5.3",      // row 5
        "NetTopologySuite 2.6.0",            // row 15, FlatAreaNearestPoint
        "Newtonsoft.Json 13.0.4",            // row 21, RunSettingsValidation
    ];

    [Fact]
    public void EachProjectHoldsOnlyThePackagesItsRowGives()
    {
        var breaches = SolutionLayout.ProjectFiles().SelectMany(FindUnlistedPackages).ToList();

        Assert.Empty(breaches);
    }

    [Fact]
    public void TheTemporaryEntryPackagesAreStillNeededAndTheirProjectsDoNotExistYet()
    {
        var entryPackages = ProjectPackages.Declared(EntryProjectFile());
        var existing = ExistingProjectNames();

        var breaches = EntryPackagesUntilTheirProjectsExist
            .Select(package => DescribeTemporaryBreach(package, entryPackages, existing))
            .OfType<string>()
            .ToList();

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

    private static IEnumerable<string> FindUnlistedPackages(string projectFile)
    {
        var name = SolutionLayout.ProjectName(projectFile);
        var row = Rules.Find(name);
        var allowed = row.Kind == ProjectRules.EntryKind ? row.Packages.Concat(EntryPackagesUntilTheirProjectsExist).ToList() : row.Packages;
        return ProjectPackages.Declared(projectFile)
            .Except(allowed)
            .Select(package => $"{name} holds {package}, which the table gives to {OwnerOf(package)}");
    }

    private static string OwnerOf(string package) =>
        Rules.Projects.FirstOrDefault(row => row.Packages.Contains(package))?.Name ?? "no project";

    private static string? DescribeTemporaryBreach(string package, IReadOnlyList<string> entryPackages, IReadOnlyList<string> existing)
    {
        if (!entryPackages.Contains(package)) return $"{package} is listed as a temporary entry package but the entry no longer holds it; delete the line";
        var owner = OwnerOf(package);
        return existing.Contains(owner) ? $"{package} belongs to {owner}, which exists now; remove it from the entry and from the list" : null;
    }

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

    private static List<string> ExistingProjectNames() =>
        SolutionLayout.ProjectFiles().Select(SolutionLayout.ProjectName).ToList();
}
