using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>Where the solution's projects are on disk, read from the repository files rather than from the build.</summary>
internal static partial class SolutionLayout
{
    private static readonly string[] GeneratedFolders = ["obj", "bin", "TestRun"];

    public static string Root() => RootFrom();

    /// <summary>Every csproj under the repository root except those in build output, as full paths.</summary>
    public static IReadOnlyList<string> ProjectFiles() =>
        Directory.EnumerateFiles(Root(), "*.csproj", SearchOption.AllDirectories)
            .Where(file => !IsInGeneratedFolder(Root(), file))
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The csproj files the .sln lists, as full paths.</summary>
    public static IReadOnlyList<string> SolutionProjectFiles() =>
        SolutionProjectPattern().Matches(File.ReadAllText(Path.Combine(Root(), "AddedObjectRemover.sln")))
            .Select(match => Path.GetFullPath(Path.Combine(Root(), match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar))))
            .Order(StringComparer.Ordinal)
            .ToList();

    public static string ProjectName(string projectFile) => Path.GetFileNameWithoutExtension(projectFile);

    public static string FolderRelativeToRoot(string projectFile) =>
        Path.GetRelativePath(Root(), Path.GetDirectoryName(projectFile)!).Replace('\\', '/');

    public static XDocument Load(string projectFile) => XDocument.Load(projectFile);

    public static string? Property(XDocument project, string name) =>
        project.Descendants(name).Select(element => element.Value.Trim()).FirstOrDefault();

    public static bool IsTestProject(string projectFile) => Property(Load(projectFile), "IsTestProject") == "true";

    /// <summary>The names of the projects this csproj references.</summary>
    public static IReadOnlyList<string> ReferencedProjectNames(string projectFile) =>
        Load(projectFile).Descendants("ProjectReference")
            .Select(reference => ProjectName(reference.Attribute("Include")!.Value.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToList();

    public static bool IsInGeneratedFolder(string baseFolder, string file)
    {
        var parts = Path.GetRelativePath(baseFolder, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.SkipLast(1).Any(GeneratedFolders.Contains);
    }

    /// <summary>This file sits in AddedObjectRemover.Tests\Architecture.</summary>
    private static string RootFrom([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    [GeneratedRegex(@"^Project\(""[^""]+""\) = ""[^""]+"", ""(?<path>[^""]+\.csproj)""", RegexOptions.Multiline)]
    private static partial Regex SolutionProjectPattern();
}
