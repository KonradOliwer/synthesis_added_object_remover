using System.Globalization;

namespace AddedObjectRemover.Tests.LogTextFormatting;

public class TextFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(8199, "8199")]
    [InlineData(1234567, "1234567")]
    [InlineData(-1500, "-1500")]
    public void CountWritesPlainDigitsWithoutGrouping(int value, string expected) => Assert.Equal(expected, TextFormat.Count(value));

    [Fact]
    public void CountAcceptsLongNumbers() => Assert.Equal("5000000000", TextFormat.Count(5_000_000_000L));

    [Theory]
    [InlineData(0, "0 mods")]
    [InlineData(1, "1 mod")]
    [InlineData(143, "143 mods")]
    public void CountWithItsNounIsSingularOnlyForOne(int value, string expected) => Assert.Equal(expected, TextFormat.Count(value, "mod", "mods"));

    [Theory]
    [InlineData(1.25, 0, "1")]
    [InlineData(1.26, 1, "1.3")]
    [InlineData(2.0, 2, "2.00")]
    [InlineData(-0.5, 1, "-0.5")]
    public void FixedWritesExactlyTheRequestedDecimals(double value, int decimals, string expected) =>
        Assert.Equal(expected, TextFormat.Fixed(value, decimals));

    [Fact]
    public void FixedAcceptsFloatNumbers() => Assert.Equal("12.5", TextFormat.Fixed(12.5f, 1));

    [Theory]
    [InlineData(0.0, "0 %")]
    [InlineData(0.25, "25 %")]
    [InlineData(1.0, "100 %")]
    public void PercentWritesTheShareAsWholePercent(double share, string expected) =>
        Assert.Equal(expected, TextFormat.Percent(share));

    [Fact]
    public void PercentRoundsToWholePercent() => Assert.Equal("13 %", TextFormat.Percent(0.126f));

    [Theory]
    [InlineData(0, "0.0s")]
    [InlineData(1500, "1.5s")]
    [InlineData(61234, "61.2s")]
    public void SecondsWritesOneDecimal(int milliseconds, string expected) =>
        Assert.Equal(expected, TextFormat.Seconds(TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public void DecimalsFollowTheCurrentCultureButCountsDoNot()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.Equal("8199", TextFormat.Count(8199));
            Assert.Equal("1,5s", TextFormat.Seconds(TimeSpan.FromMilliseconds(1500)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
