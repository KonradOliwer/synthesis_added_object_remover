using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>The patcher project's source files, for tests that guard where certain code may appear.</summary>
internal static class ProductionSources
{
    public static IEnumerable<string> Files()
    {
        var projectFolder = Path.Combine(TestsFolder(), "..", "AddedObjectRemover");
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(projectFolder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}") && !file.Contains($"{separator}bin{separator}"))
            .Select(Path.GetFullPath);
    }

    private static string TestsFolder([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, ".."));
}
