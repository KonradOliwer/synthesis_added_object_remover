namespace AddedObjectRemover.Tests.Architecture;

/// <summary>The string literal readers of <see cref="SourceFile"/> that the wording tests rely on.</summary>
public class SourceFileReaderTests
{
    [Fact]
    public void TheLiteralReaderSeesPlainVerbatimAndInterpolatedTextButNotComments()
    {
        var literals = SourceFile.StringLiteralsOf("var a = \"one\"; // \"no\"\n/* \"no\" */ var b = @\"two\" + $\"three {a}\" + 'c';");

        Assert.Equal(["one", "two", "three {a}"], literals);
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

        var literals = SourceFile.WordingLiteralLinesOf(code);

        Assert.Equal([("kept one", 5), ("kept two", 6), ("kept three", 7), ("kept four", 8), (")", 9), ("kept five", 9)], literals);
    }
}
