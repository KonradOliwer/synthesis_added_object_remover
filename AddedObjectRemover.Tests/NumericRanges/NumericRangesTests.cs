namespace AddedObjectRemover.Tests.NumericRanges;

public class NumericRangesTests
{
    [Theory]
    [InlineData(3, 5, 60, true)]
    [InlineData(2, 5, 60, false)]
    [InlineData(1, 2, 50, true)]
    [InlineData(0, 4, 0, true)]
    [InlineData(0, 4, 10, false)]
    [InlineData(4, 4, 100, true)]
    public void IntegerShareReachesThePercentExactlyAtTheBoundary(int part, int whole, int percent, bool expected) =>
        Assert.Equal(expected, SharePercent.AtLeast(part, whole, percent));

    [Theory]
    [InlineData(5f, 10f, 50, true)]
    [InlineData(4.99f, 10f, 50, false)]
    [InlineData(0f, 0f, 50, true)]
    [InlineData(3f, 3f, 100, true)]
    public void FloatShareReachesThePercentExactlyAtTheBoundary(float part, float whole, int percent, bool expected) =>
        Assert.Equal(expected, SharePercent.AtLeast(part, whole, percent));

    [Fact]
    public void FloatShareCompareKeepsFloatArithmetic()
    {
        const float part = 0.1f;
        const float whole = 0.3f;

        Assert.Equal(part * Percent.PerWhole >= 33 * whole, SharePercent.AtLeast(part, whole, 33));
    }

    [Theory]
    [InlineData(0.5f, 0.5f, 0f, true)]
    [InlineData(0.4999f, 0.5f, 0f, false)]
    [InlineData(0.499995f, 0.5f, 0.00001f, true)]
    [InlineData(0.4999f, 0.5f, 0.00001f, false)]
    [InlineData(0.6f, 0.5f, 0.00001f, true)]
    public void TolerantAtLeastAcceptsValuesUpToEpsilonBelowTheThreshold(float value, float threshold, float epsilon, bool expected) =>
        Assert.Equal(expected, Tolerant.AtLeast(value, threshold, epsilon));
}
