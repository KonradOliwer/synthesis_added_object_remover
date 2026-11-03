using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Compares output with the expected-output files checked in next to this source file. A missing
/// expected-output file is created from the output, and the test fails so it gets reviewed. On a
/// difference the actual output and its changed lines are written to TestRun\ExpectedOutput (ignored
/// by git), to review and copy over the expected-output file when the change is intended.
/// </summary>
/// <remarks>
/// While TestRun\write-expected-output.txt (ignored by git) holds exactly "on", the output is written over the
/// expected-output files that differ, keeping each file's line ending; any other content, or no file, switches it off.
/// A test that writes still fails, so the written files get reviewed; in a class marked
/// <see cref="ExpectedOutputTestAttribute"/> it fails after its last comparison, so it writes all its files in one run.
/// <see cref="ExpectedOutputWriteSwitchTests"/> fails while the switch is on, so a run with it is never green. A name
/// compared twice fails, so two tests cannot silently overwrite each other's file, and
/// TestRun\ExpectedOutput\not-compared.txt lists the expected-output files no test compared.
/// </remarks>
internal static class ExpectedOutputFiles
{
    private const string TestRunFolderName = "TestRun";
    private const string WriteSwitchFileName = "write-expected-output.txt";
    private const string WriteSwitchOn = "on";
    private const string ExpectedOutputFolderName = "ExpectedOutput";
    private const string ExpectedOutputExtension = ".txt";
    private const string ActualExtension = ".actual.txt";
    private const string ChangesExtension = ".changes.txt";
    private const string NotComparedListName = "not-compared.txt";
    private const string WindowsLineEnding = "\r\n";
    private const string UnixLineEnding = "\n";

    private static readonly Lock ComparedLock = new();
    private static readonly HashSet<string> ComparedNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The expected-output files the running test wrote; null outside a test of a class marked <see cref="ExpectedOutputTestAttribute"/>.</summary>
    private static readonly AsyncLocal<List<string>?> WrittenByThisTest = new();

    public static string WriteSwitchPath() => Path.Combine(TestRunFolder(), WriteSwitchFileName);

    public static bool IsWriteSwitchOn() =>
        File.Exists(WriteSwitchPath()) && File.ReadAllText(WriteSwitchPath()) == WriteSwitchOn;

    public static void StartTest() => WrittenByThisTest.Value = [];

    /// <summary>Fails when the test that ends wrote any expected-output file.</summary>
    public static void EndTest()
    {
        var written = WrittenByThisTest.Value;
        WrittenByThisTest.Value = null;
        if (written is { Count: > 0 }) Assert.Fail(DescribeWritten(written));
    }

    public static void AssertMatches(string name, IReadOnlyList<string> actual)
    {
        var expectedPath = ExpectedPath(name);
        if (!IsWriteSwitchOn())
        {
            AssertMatchesExpectedFile(name, expectedPath, actual);
            return;
        }

        RecordComparison(name);
        if (File.Exists(expectedPath) && File.ReadAllLines(expectedPath).SequenceEqual(actual, StringComparer.Ordinal)) return;
        WriteKeepingLineEnding(expectedPath, actual);
        if (WrittenByThisTest.Value is { } written) written.Add(expectedPath);
        else Assert.Fail(DescribeWritten([expectedPath]));
    }

    private static string DescribeWritten(IEnumerable<string> paths) =>
        $"wrote the output over {string.Join(", ", paths)}. Review with git diff, then empty {WriteSwitchPath()} and run the tests again.";

    private static void AssertMatchesExpectedFile(string name, string expectedPath, IReadOnlyList<string> actual)
    {
        if (!File.Exists(expectedPath))
        {
            WriteKeepingLineEnding(expectedPath, actual);
            Assert.Fail($"{name}: created the expected-output file {expectedPath}; review it, then run the tests again.");
        }

        var expectedLines = File.ReadAllLines(expectedPath);
        if (expectedLines.SequenceEqual(actual, StringComparer.Ordinal)) return;

        var actualPath = Path.Combine(ActualFolder(), name + ActualExtension);
        Write(actualPath, actual);
        Write(Path.Combine(ActualFolder(), name + ChangesExtension), DescribeChangedLines(expectedLines, actual));
        Assert.Fail($"{name}: {DescribeFirstDifference(expectedLines, actual)}. Expected output: {expectedPath}. Actual: {actualPath}.");
    }

    /// <summary>
    /// Fails on a name compared before in this run, then rewrites the list of expected-output files not yet compared;
    /// the last comparison of the run leaves the complete list.
    /// </summary>
    private static void RecordComparison(string name)
    {
        lock (ComparedLock)
        {
            if (!ComparedNames.Add(name))
                Assert.Fail($"{name}: compared a second time in this run; two tests build the same expected-output file name.");
            Write(Path.Combine(ActualFolder(), NotComparedListName), FindNotComparedNames());
        }
    }

    private static List<string> FindNotComparedNames() =>
    [
        .. Directory.EnumerateFiles(ExpectedFolder(), "*" + ExpectedOutputExtension, SearchOption.AllDirectories)
            .Select(NameOf)
            .Where(name => !ComparedNames.Contains(name))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The name a test passes for the expected-output file at <paramref name="path"/>.</summary>
    private static string NameOf(string path) =>
        Path.GetRelativePath(ExpectedFolder(), path)[..^ExpectedOutputExtension.Length].Replace('\\', '/');

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

    /// <summary>
    /// Writes an expected-output file with the line ending it already has (Windows or Unix, a last line break or none), so
    /// git shows only the lines that changed; a new file gets Unix line endings and a last line break.
    /// </summary>
    private static void WriteKeepingLineEnding(string path, IReadOnlyList<string> lines)
    {
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        var lineEnding = existing is not null && existing.Contains(WindowsLineEnding, StringComparison.Ordinal) ? WindowsLineEnding : UnixLineEnding;
        var endsWithLineBreak = existing is null || existing.Length == 0 || existing.EndsWith(UnixLineEnding, StringComparison.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join(lineEnding, lines) + (endsWithLineBreak && lines.Count > 0 ? lineEnding : ""));
    }

    private static string DescribeFirstDifference(IReadOnlyList<string> expectedLines, IReadOnlyList<string> actual)
    {
        var line = expectedLines.Zip(actual).TakeWhile(pair => pair.First == pair.Second).Count();
        var expected = line < expectedLines.Count ? expectedLines[line] : "(end of file)";
        var found = line < actual.Count ? actual[line] : "(end of output)";
        return $"line {line + 1} expected '{expected}' but was '{found}'";
    }

    private static string ExpectedPath(string name) => Path.Combine(ExpectedFolder(), name + ExpectedOutputExtension);

    private static string ExpectedFolder() => Path.Combine(SourceFolder(), ExpectedOutputFolderName);

    private static string ActualFolder() => Path.Combine(TestRunFolder(), ExpectedOutputFolderName);

    private static string TestRunFolder() => Path.Combine(RepositoryFolder(), TestRunFolderName);

    private static string SourceFolder([CallerFilePath] string sourceFile = "") => Path.GetDirectoryName(sourceFile)!;

    /// <summary>This file sits in AddedObjectRemover.Tests\EndToEnd.</summary>
    private static string RepositoryFolder() => Path.GetFullPath(Path.Combine(SourceFolder(), "..", ".."));
}
