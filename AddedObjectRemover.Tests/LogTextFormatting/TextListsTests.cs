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
    public void CountsWriteTheNumberBeforeTheNameWithDigitGrouping() =>
        Assert.Equal(
            ["1,234 Lights", "2 Markers"],
            TextLists.Counts(new[] { KeyValuePair.Create("Lights", 1234), KeyValuePair.Create("Markers", 2) }));

    [Fact]
    public void CountsWriteEnumNamesLikeText() =>
        Assert.Equal(["5 Friday"], TextLists.Counts(new[] { KeyValuePair.Create(DayOfWeek.Friday, 5) }));

    [Fact]
    public void TopNTakesTheFirstItemsAndFormatsEachOne() =>
        Assert.Equal("a:1; b:2", TextLists.TopN(new[] { ("a", 1), ("b", 2), ("c", 3) }, 2, item => $"{item.Item1}:{item.Item2}", "; "));

    [Fact]
    public void TopNWithFewerItemsThanTheLimitTakesAll() =>
        Assert.Equal("a", TextLists.TopN(new[] { "a" }, 5, item => item, "; "));
}
