namespace AddedObjectRemover.Tests.LogTextFormatting;

public class TextListsTests
{
    [Fact]
    public void JoinOrJoinsWithTheGivenSeparator() =>
        Assert.Equal("a|b|c", TextLists.JoinOr(["a", "b", "c"], "|", "nothing"));

    [Fact]
    public void JoinOrWritesTheGivenTextForNoItems() =>
        Assert.Equal("nothing", TextLists.JoinOr([], "|", "nothing"));

    [Fact]
    public void JoinOrKeepsASingleItemAsIs() =>
        Assert.Equal("a", TextLists.JoinOr(["a"], "|", "nothing"));

    [Fact]
    public void CappedListsEverythingWithinTheLimit() =>
        Assert.Equal("a; b", TextLists.Capped(["a", "b"], 2, "; ", hidden => $"+{hidden}"));

    [Fact]
    public void CappedAppendsTheMoreTextAsLastItemWhenOverTheLimit() =>
        Assert.Equal("a; b; (+3 more)", TextLists.Capped(["a", "b", "c", "d", "e"], 2, "; ", hidden => $"(+{hidden} more)"));

    [Fact]
    public void CountsWriteTheNumberBeforeTheNameWithoutDigitGrouping() =>
        Assert.Equal(
            ["1234 Lights", "2 Markers"],
            TextLists.Counts(new[] { KeyValuePair.Create("Lights", 1234), KeyValuePair.Create("Markers", 2) }));

    [Fact]
    public void CountsWriteEnumNamesLikeText() =>
        Assert.Equal(["5 Friday"], TextLists.Counts(new[] { KeyValuePair.Create(DayOfWeek.Friday, 5) }));

    [Fact]
    public void JoinCountsWritesTheCountOfEachEntryBeforeItsNameAndJoinsThem() =>
        Assert.Equal("2 a, 1 b", TextLists.JoinCounts(new[] { ("a", 2), ("b", 1) }, entry => entry.Item1, entry => entry.Item2, ", "));

    [Fact]
    public void HeadedWritesTheHeadingAndTheJoinedItemsAsOneSentence() =>
        Assert.Equal(["Found: a; b."], TextLists.Headed("Found", ["a", "b"], "; "));

    [Fact]
    public void HeadedWritesNothingForNoItems() =>
        Assert.Empty(TextLists.Headed("Found", [], "; "));

    [Fact]
    public void TopNTakesTheFirstItemsAndFormatsEachOne() =>
        Assert.Equal("a:1; b:2", TextLists.TopN(new[] { ("a", 1), ("b", 2), ("c", 3) }, 2, item => $"{item.Item1}:{item.Item2}", "; "));

    [Fact]
    public void TopNWithFewerItemsThanTheLimitTakesAll() =>
        Assert.Equal("a", TextLists.TopN(new[] { "a" }, 5, item => item, "; "));

    [Flags]
    private enum Sides
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
    }

    [Fact]
    public void JoinSetFlagsNamesTheSetFlagsInTheEnumsOrder() =>
        Assert.Equal("Left; Top", TextLists.JoinSetFlags(Sides.Top | Sides.Left, flag => flag.ToString(), "; "));

    [Fact]
    public void JoinSetFlagsLeavesOutTheZeroValue() =>
        Assert.Equal("", TextLists.JoinSetFlags(Sides.None, flag => flag.ToString(), "; "));
}
