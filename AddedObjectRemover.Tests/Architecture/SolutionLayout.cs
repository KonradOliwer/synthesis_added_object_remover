using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>Where the solution's projects are on disk, read from the repository files rather than from the build.</summary>
internal static partial class SolutionLayout
{
    /// <summary>Build and test output, and .claude, whose worktrees hold whole copies of the repository.</summary>
    private static readonly string[] GeneratedFolders = ["obj", "bin", "TestRun", ".claude"];

    private static readonly Lazy<IReadOnlyList<string>> AllProjectFiles = new(FindProjectFiles);

    public static string Root() => RootFrom();

    public static string ProductionFolder() => Path.Combine(Root(), "AddedObjectRemover");

    /// <summary>Every csproj under the repository root except those in build output, as full paths. Found once per test run.</summary>
    public static IReadOnlyList<string> ProjectFiles() => AllProjectFiles.Value;

    /// <summary>The files matching the pattern under the folder at any depth, never entering the generated folders.</summary>
    public static IReadOnlyList<string> FilesBelow(string folder, string searchPattern)
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            found.AddRange(Directory.EnumerateFiles(current, searchPattern));
            foreach (var subfolder in Directory.EnumerateDirectories(current).Where(path => !GeneratedFolders.Contains(Path.GetFileName(path))))
            {
                pending.Push(subfolder);
            }
        }

        return found.Select(Path.GetFullPath).Order(StringComparer.Ordinal).ToList().AsReadOnly();
    }

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

    private static IReadOnlyList<string> FindProjectFiles() => FilesBelow(Root(), "*.csproj");

    /// <summary>This file sits in AddedObjectRemover.Tests\Architecture.</summary>
    private static string RootFrom([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    [GeneratedRegex(@"^Project\(""[^""]+""\) = ""[^""]+"", ""(?<path>[^""]+\.csproj)""", RegexOptions.Multiline)]
    private static partial Regex SolutionProjectPattern();
}
