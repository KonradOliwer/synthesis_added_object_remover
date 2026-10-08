using System.Text.Json;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>The packages of a project, read from its csproj and from the restore result next to it.</summary>
internal static class ProjectPackages
{
    /// <summary>"name version" for each PackageReference, spelled as in the project table.</summary>
    public static IReadOnlyList<string> Declared(string projectFile) =>
        SolutionLayout.Load(projectFile).Descendants("PackageReference")
            .Select(reference => $"{reference.Attribute("Include")!.Value} {reference.Attribute("Version")!.Value}")
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The names of the packages the project compiles against, directly or through other packages and projects
    /// (the compile set of obj/project.assets.json).
    /// </summary>
    /// <exception cref="FileNotFoundException">The project was not restored, so there is nothing to check.</exception>
    public static IReadOnlyList<string> CompiledAgainst(string projectFile)
    {
        var assetsFile = Path.Combine(Path.GetDirectoryName(projectFile)!, "obj", "project.assets.json");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var target = assets.RootElement.GetProperty("targets").EnumerateObject().Single().Value;
        return target.EnumerateObject()
            .Where(library => IsPackageWithCompileAssemblies(library.Value))
            .Select(library => library.Name[..library.Name.IndexOf('/')])
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsPackageWithCompileAssemblies(JsonElement library) =>
        library.GetProperty("type").GetString() == "package"
            && library.TryGetProperty("compile", out var compile)
            && compile.EnumerateObject().Any();
}
