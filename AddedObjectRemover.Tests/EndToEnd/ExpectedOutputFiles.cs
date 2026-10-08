using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Compares output with the expected-output files checked in next to this source file. A missing
/// expected-output file is created from the output, and the test fails so it gets reviewed. On a
/// difference the actual output and its changed lines are written to TestRun\ExpectedOutput (ignored
/// by git), to review and copy over the expected-output file when the change is intended.
/// </summary>
internal static class ExpectedOutputFiles
{
    private const string ExpectedOutputFolderName = "ExpectedOutput";
    private const string ExpectedOutputExtension = ".txt";
    private const string ActualExtension = ".actual.txt";
    private const string ChangesExtension = ".changes.txt";

    public static void AssertMatches(string name, IReadOnlyList<string> actual)
    {
        var expectedPath = Path.Combine(SourceFolder(), ExpectedOutputFolderName, name + ExpectedOutputExtension);
        if (!File.Exists(expectedPath))
        {
            Write(expectedPath, actual);
            Assert.Fail($"{name}: created the expected-output file {expectedPath}; review it, then run the tests again.");
        }

        var expectedLines = File.ReadAllLines(expectedPath);
        if (expectedLines.SequenceEqual(actual, StringComparer.Ordinal)) return;

        var actualFolder = Path.Combine(RepositoryFolder(), "TestRun", ExpectedOutputFolderName);
        var actualPath = Path.Combine(actualFolder, name + ActualExtension);
        Write(actualPath, actual);
        Write(Path.Combine(actualFolder, name + ChangesExtension), DescribeChangedLines(expectedLines, actual));
        Assert.Fail($"{name}: {DescribeFirstDifference(expectedLines, actual)}. Expected output: {expectedPath}. Actual: {actualPath}.");
    }

    /// <summary>The lines only the expected-output file has ("- ") and only the output has ("+ "), ignoring order; empty when only the order differs.</summary>
    private static List<string> DescribeChangedLines(IReadOnlyList<string> expectedLines, IReadOnlyList<string> actual) =>
    [
        .. LinesMissingFrom(actual, expectedLines).Select(line => "- " + line),
        .. LinesMissingFrom(expectedLines, actual).Select(line => "+ " + line),
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

    private static string DescribeFirstDifference(IReadOnlyList<string> expectedLines, IReadOnlyList<string> actual)
    {
        var line = expectedLines.Zip(actual).TakeWhile(pair => pair.First == pair.Second).Count();
        var expected = line < expectedLines.Count ? expectedLines[line] : "(end of file)";
        var found = line < actual.Count ? actual[line] : "(end of output)";
        return $"line {line + 1} expected '{expected}' but was '{found}'";
    }

    private static string SourceFolder([CallerFilePath] string sourceFile = "") => Path.GetDirectoryName(sourceFile)!;

    /// <summary>This file sits in AddedObjectRemover.Tests\EndToEnd.</summary>
    private static string RepositoryFolder() => Path.GetFullPath(Path.Combine(SourceFolder(), "..", ".."));
}
