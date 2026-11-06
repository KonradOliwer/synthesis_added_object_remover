using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.RecordParts;

/// <summary>Heights encoded as a LAND height map read back as they were, within the steps the map can store.</summary>
public class LandHeightEncoderTests
{
    private const int Side = TerrainGrid.VerticesPerSide;
    private const float Step = TerrainGrid.UnitsPerHeightStep;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SeededHeightsReadBackWithinHalfAStep(int seed)
    {
        var random = new Random(seed);
        var heights = Heights((x, y) => 1000f + 300f * MathF.Sin(x * 0.2f) + 150f * MathF.Cos(y * 0.3f) + (float)random.NextDouble() * 20f);

        var read = Decode(heights);

        Assert.All(Enumerable.Range(0, heights.Length), point => Assert.InRange(read[point] - heights[point], -Step / 2, Step / 2));
    }

    [Fact]
    public void HeightsAWholeNumberOfStepsFromTheFirstPointReadBackExactly()
    {
        var heights = Heights((x, y) => -2003f + Step * (x * 3 - y * 2));

        Assert.Equal(heights, Decode(heights));
    }

    /// <summary>The first point lies a fraction of a step up; every other height is put on the steps from it and is stored exactly.</summary>
    [Fact]
    public void HeightsPutOnTheStepsOfTheFirstPointReadBackExactly()
    {
        const float First = 25.5f;
        var random = new Random(4);
        var heights = Heights((x, y) => x == 0 && y == 0 ? First : LandHeightEncoder.NearestOnTheStepsOf(First + 300f * (float)random.NextDouble(), First));

        Assert.Equal(heights, Decode(heights));
        Assert.Equal((First + Step, First), (LandHeightEncoder.NearestOnTheStepsOf(First + Step / 2, First), LandHeightEncoder.NearestOnTheStepsOf(First + Step / 2 - 0.01f, First)));
    }

    [Fact]
    public void TheLargestStepsASignedByteHoldsAreStored()
    {
        var heights = Heights((x, y) => Step * (x % 2 == 0 ? 0 : sbyte.MaxValue) + Step * sbyte.MinValue * y);

        Assert.Equal(heights, Decode(heights));
    }

    /// <summary>The heights a planned record holds are those a LAND record written with them reads back.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void TheStoredHeightsAreTheHeightsAWrittenRecordReadsBack(int seed)
    {
        var random = new Random(seed);
        var heights = Heights((x, y) => -500f + 200f * MathF.Sin(x * 0.3f) + (float)random.NextDouble() * 30f + y);

        Assert.Equal(Decode(heights), LandHeightEncoder.Stored(heights));
    }

    [Fact]
    public void HeightsThatCannotBeStoredHaveNoStoredHeights() =>
        Assert.Null(LandHeightEncoder.Stored(Heights((x, _) => x == 10 ? Step * (sbyte.MaxValue + 1) : 0f)));

    [Fact]
    public void AStepAlongARowBeyondASignedByteCannotBeStored()
    {
        var heights = Heights((x, _) => x == 10 ? Step * (sbyte.MaxValue + 1) : 0f);

        Assert.False(LandHeightEncoder.CanStore(heights));
        Assert.Null(LandHeightEncoder.Encode(heights));
    }

    [Fact]
    public void AStepBetweenRowsBeyondASignedByteCannotBeStored()
    {
        var heights = Heights((_, y) => y == 5 ? Step * (sbyte.MinValue - 1) : 0f);

        Assert.False(LandHeightEncoder.CanStore(heights));
    }

    private static float[] Heights(Func<int, int, float> heightAt) =>
        [.. Enumerable.Range(0, Side * Side).Select(point => heightAt(point % Side, point / Side))];

    private static float[] Decode(float[] heights) => TestLandHeights.Decode(heights);
}
