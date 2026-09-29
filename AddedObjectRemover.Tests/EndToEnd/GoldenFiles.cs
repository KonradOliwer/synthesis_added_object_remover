using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Compares output with the golden files checked in next to this source file. A missing golden
/// file is created from the output, and the test fails so it gets reviewed. On a difference the
/// actual output and its changed lines are written to TestRun\Golden (ignored by git), to review
/// and copy over the golden file when the change is intended.
/// </summary>
internal static class GoldenFiles
{
    private const string GoldenFolderName = "Golden";
    private const string GoldenExtension = ".txt";
    private const string ActualExtension = ".actual.txt";
    private const string ChangesExtension = ".changes.txt";

    public static void AssertMatches(string name, IReadOnlyList<string> actual)
    {
        var goldenPath = Path.Combine(SourceFolder(), GoldenFolderName, name + GoldenExtension);
        if (!File.Exists(goldenPath))
        {
            Write(goldenPath, actual);
            Assert.Fail($"{name}: created the golden file {goldenPath}; review it, then run the tests again.");
        }

        var golden = File.ReadAllLines(goldenPath);
        if (golden.SequenceEqual(actual, StringComparer.Ordinal)) return;

        var actualFolder = Path.Combine(RepositoryFolder(), "TestRun", GoldenFolderName);
        var actualPath = Path.Combine(actualFolder, name + ActualExtension);
        Write(actualPath, actual);
        Write(Path.Combine(actualFolder, name + ChangesExtension), DescribeChangedLines(golden, actual));
        Assert.Fail($"{name}: {DescribeFirstDifference(golden, actual)}. Golden: {goldenPath}. Actual: {actualPath}.");
    }

    /// <summary>The lines only the golden file has ("- ") and only the output has ("+ "), ignoring order; empty when only the order differs.</summary>
    private static List<string> DescribeChangedLines(IReadOnlyList<string> golden, IReadOnlyList<string> actual) =>
    [
        .. LinesMissingFrom(actual, golden).Select(line => "- " + line),
        .. LinesMissingFrom(golden, actual).Select(line => "+ " + line),
    ];

    /// <returns>The lines of <paramref name="source"/>, in order, that <paramref name="other"/> does not have as often.</returns>
    private static List<string> LinesMissingFrom(IReadOnlyList<string> other, IReadOnlyList<string> source)
    {
        var available = other.GroupBy(line => line, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var line in source)
        {
            if (available.TryGetValue(line, out var count) && count > 0) available[line] = count - 1;
            else missing.Add(line);
        }
        return missing;
    }

    private static void Write(string path, IReadOnlyList<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }

    private static string DescribeFirstDifference(IReadOnlyList<string> golden, IReadOnlyList<string> actual)
    {
        var line = golden.Zip(actual).TakeWhile(pair => pair.First == pair.Second).Count();
        var expected = line < golden.Count ? golden[line] : "(end of file)";
        var found = line < actual.Count ? actual[line] : "(end of output)";
        return $"line {line + 1} expected '{expected}' but was '{found}'";
    }

    private static string SourceFolder([CallerFilePath] string sourceFile = "") => Path.GetDirectoryName(sourceFile)!;

    /// <summary>This file sits in AddedObjectRemover.Tests\EndToEnd.</summary>
    private static string RepositoryFolder() => Path.GetFullPath(Path.Combine(SourceFolder(), "..", ".."));
}
