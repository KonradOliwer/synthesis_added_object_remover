namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// No business code is copied: no 40 consecutive tokens (names and numbers normalised, comments ignored) appear
/// twice. Limit: shorter copies that the known-idiom list does not name are not found. Repetition that is
/// structure rather than logic is listed with its reason, by the key printed for it.
/// </summary>
public class CopiedCodeTests
{
    private const int WindowLength = 40;

    private const string CountLines = "One count per line of a statistics record; each line names its own kind, so there is no logic to share.";
    private const string EnumTextArms = "Enum-to-text switch arms ending in the same unreachable-case throw; each switch lists the values of its own enum.";
    private const string CsvColumnNames = "CSV header column names; each table lists its own columns.";
    private const string CounterRecord = "Counter records: each lists its own counters and its sum, which only repeat in shape.";
    private const string SectionOneLiners = "One-line log section factories (a name and the message lines of their input); no logic to share.";

    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["69c3a048"] = CountLines,
        ["3c3e351b"] = CountLines,
        ["838dc0ce"] = "Setting-by-setting check lines; each reads another setting with its own default, limit and wording.",
        ["51c0267e"] = EnumTextArms,
        ["38364156"] = EnumTextArms,
        ["41674f6a"] = EnumTextArms,
        ["02151952"] = EnumTextArms,
        ["6133e948"] = EnumTextArms,
        ["4a06039b"] = SectionOneLiners,
        ["a1996b85"] = "Declaration of a report class with a constant or header list; the tables share only the shape of the declaration.",
        ["cc66e963"] = CsvColumnNames,
        ["495d7d50"] = CsvColumnNames,
        ["32074483"] = CounterRecord,
        ["4562028d"] = CounterRecord,
    };

    [Fact]
    public void NoBusinessCodeIsCopiedExceptTheListed()
    {
        var files = BusinessCode.SourceFiles().Select(file => (file.Description, File.ReadAllText(file.FullPath))).ToList();

        var runs = CodeCopies.Find(files, WindowLength);

        var unlisted = runs.Where(run => !Allowed.ContainsKey(run.Key)).Select(run => run.ToString()).ToList();
        var stale = Allowed.Keys.Except(runs.Select(run => run.Key)).Order(StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0, $"Copied code (reuse a tool or share one method, or list its key with a reason):{Environment.NewLine}{string.Join(Environment.NewLine, unlisted)}");
        Assert.True(stale.Count == 0, $"Listed but no longer copied:{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
    }

    [Fact]
    public void TheScanFindsACopyWithRenamedNamesAndOtherComments()
    {
        const string first = """
            // one
            int Total(List<int> values)
            {
                var sum = 0;
                foreach (var value in values) { if (value > 3) sum += value * 2; }
                return sum + 7;
            }
            """;
        const string second = """
            void Other() { }
            /* two */
            int Count(List<int> numbers)
            {
                var result = 0;
                foreach (var number in numbers) { if (number > 9) result += number * 5; } // note
                return result + 1;
            }
            """;

        var runs = CodeCopies.Find([("a", first), ("b", second)], windowLength: 20);

        Assert.Equal(["a", "b"], runs.Select(run => run.File));
        Assert.Equal(runs[0].Key, runs[1].Key);
        Assert.Equal((2, 6), (runs[0].FirstLine, runs[0].LastLine));
        Assert.Equal((3, 7), (runs[1].FirstLine, runs[1].LastLine));
    }

    [Fact]
    public void TheScanFindsACopyInsideOneFileButNotOverlappingRepetition()
    {
        const string copiedTwice = "int A(int x) { return x + 1 * 2 - 3; } int B(int y) { return y + 1 * 2 - 3; }";
        const string repeatingPattern = "a(); a(); a(); a();";

        Assert.Equal(2, CodeCopies.Find([("a", copiedTwice)], windowLength: 10).Count);
        Assert.Empty(CodeCopies.Find([("a", repeatingPattern)], windowLength: 10));
    }

    [Fact]
    public void TheScanIgnoresDifferentCodeAndCodeShorterThanTheWindow()
    {
        var runs = CodeCopies.Find([("a", "int A(int x) { return x + 1; }"), ("b", "int B(int x) { return x - 1; }")], windowLength: 20);

        Assert.Empty(runs);
    }

    [Fact]
    public void TheTokenizerKeepsEachLiteralAsOneTokenAndNormalisesNames()
    {
        const string source = """"
            var a = "x { y " + @"p ""q"" r" + $"v {Get("}")} w" + """raw "quote" """ + 'c' + '\'' + 12.5f; // tail
            /* block
               comment */
            """";

        var tokens = CodeTokens.Tokenize(source).Select(token => token.Text);

        Assert.Equal(
            ["var", "name", "=", "text", "+", "text", "+", "text", "+", "text", "+", "text", "+", "text", "+", "number", ";"],
            tokens);
    }

    [Fact]
    public void TheTokenizerKeepsTheLineOfEachTokenAcrossCommentsAndMultiLineStrings()
    {
        const string source = "a /* x\ny */ b\n@\"p\nq\" c";

        var lines = CodeTokens.Tokenize(source).Select(token => token.Line);

        Assert.Equal([1, 2, 3, 4], lines);
    }

    [Fact]
    public void TheScanSkipsLinesOfBracesUsingsAndAttributes()
    {
        const string source = """
            using System;
            using var file = Open();
            [Fact]
            {
            }
            return 1;
            """;

        var lines = CodeTokens.Significant(source).Select(token => token.Line).Distinct();

        Assert.Equal([2, 6], lines);
    }
}
