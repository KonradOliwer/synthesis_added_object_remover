namespace AddedObjectRemover.Tests.Architecture;

/// <summary>The literal and member readers of <see cref="SourceFile"/>.</summary>
public class SourceFileReaderTests
{
    [Fact]
    public void TheLiteralReaderSeesTheTextOfEveryStringFormButNotCharactersOrComments()
    {
        const string code = """"
            var a = "plain" + @"say ""hi""" + @"""x""" + """raw "quoted" text""" + $"hole {a} end" + 'c'; // "comment"
            """";

        var literals = SourceFile.OfSnippet(code).StringLiterals;

        Assert.Equal(["plain", "say \"hi\"", "\"x\"", "raw \"quoted\" text", "hole {a} end"], literals);
    }

    [Fact]
    public void TheWordingReaderLeavesOutOnlyTheArgumentsOfAThrownStandardBugException()
    {
        const string code = """
            var a = x ?? throw new InvalidOperationException(
                $"bug one {Describe(x)}" + "bug two");
            throw new System.Diagnostics.UnreachableException("bug three");
            throw new ArgumentOutOfRangeException(nameof(x), x, "bug four");
            Log("kept one"); // throw new InvalidOperationException("comment")
            throw new InvalidDataException("kept two");
            throw new SettingsUnreadableException("kept three");
            var b = new InvalidOperationException("kept four");
            Log(")", "kept five");
            """;

        var literals = SourceFile.OfSnippet(code).WordingLiteralLines;

        Assert.Equal([("kept one", 5), ("kept two", 6), ("kept three", 7), ("kept four", 8), (")", 9), ("kept five", 9)], literals);
    }

    [Fact]
    public void TheMemberReadersSeeAReceiverWrittenAloneOrAsTheLastPartOfAQualifiedName()
    {
        var file = SourceFile.OfSnippet("""
            System.Console.WriteLine(1);
            var now = global::System.DateTime.UtcNow;
            Order.LoadOrder.Import(path);
            Parallel.For(0, 1, Use);
            var other = ConsoleLike.Write;
            """);

        Assert.True(file.ReadsOn("Console"));
        Assert.True(file.Reads("DateTime", "UtcNow"));
        Assert.True(file.Reads("LoadOrder", "Import"));
        Assert.True(file.ReadsOn("Parallel"));
        Assert.False(file.Reads("Console", "Write"));
        Assert.False(file.ReadsOn("Load"));
    }

    [Fact]
    public void TheArgumentReaderSeesACallInsideTheArgumentsOfAPlainOrQualifiedCall()
    {
        var file = SourceFile.OfSnippet("""
            Failures.Attempt(() => Add(1));
            Ns.Failures.Attempt(() => Add(2));
            Failures.Other(() => Add(3));
            Add(4);
            """);

        var inside = file.CallsOf("Add").Select(call => SyntaxNames.IsInArgumentsOfCallTo(call, "Failures", "Attempt"));

        Assert.Equal([true, true, false, false], inside);
    }

    [Fact]
    public void TheReadReaderSeesANullConditionalReadAndLeavesOutOnlyTheExcludedReceiver()
    {
        Assert.True(SourceFile.OfSnippet("var a = record?.EnableParent;").ReadsExceptOn("EnableParent", "RelationKind"));
        Assert.True(SourceFile.OfSnippet("var a = record.EnableParent;").ReadsExceptOn("EnableParent", "RelationKind"));
        Assert.False(SourceFile.OfSnippet("var a = RelationKind.EnableParent;").ReadsExceptOn("EnableParent", "RelationKind"));
    }
}
