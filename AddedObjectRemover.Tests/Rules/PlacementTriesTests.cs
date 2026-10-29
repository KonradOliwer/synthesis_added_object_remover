using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The placements a clashing object is tried at, in order, for each setting of the shrink limit and the turn limit.</summary>
public class PlacementTriesTests
{
    private static readonly EulerPlacement New = new(new Vector3(100, 0, 0), Vector3.Zero, 1f);
    private static readonly TargetObject Own = TestTargets.Create(0, New.Transform);

    [Fact]
    public void AnObjectIsTriedShrunkWhereItStands()
    {
        var tries = Tries(new FitSettings(90, 0), Own);

        Assert.Equal([0.95f, 0.9f], tries.Select(placement => placement.Scale), FloatComparer);
        Assert.All(tries, placement => Assert.Equal(New.Position, placement.Position));
    }

    [Fact]
    public void AnObjectOfAnotherSizeIsShrunkByShareOfItsOwnSize()
    {
        var large = TestTargets.Create(0, TestTargets.At(New.Position, scale: 2f));

        Assert.Equal([1.9f, 1.8f], Tries(new FitSettings(90, 0), large).Select(placement => placement.Scale), FloatComparer);
    }

    [Fact]
    public void ADifferentKindOfObjectIsTriedTheSame() =>
        Assert.Equal(Tries(new FitSettings(90, 0), Own), Tries(new FitSettings(90, 0), Own with { Origin = TargetOrigin.Moved }));

    [Fact]
    public void TheShrinkLimitItselfIsTheLastSize() =>
        Assert.Equal([0.95f, 0.9f, 0.85f, 0.83f], Sizes(new FitSettings(83, 0)), FloatComparer);

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void AShrinkLimitOf100OrMoreNeverShrinks(int limit) =>
        Assert.Empty(Tries(new FitSettings(limit, 0), Own));

    [Fact]
    public void AShrinkLimitOf100AndATurnLimitOf0TryNothing() =>
        Assert.Empty(Tries(new FitSettings(100, 0), Own));

    [Fact]
    public void TurnsGoFromTheSmallestUpOneWayBeforeTheOtherTheLimitItselfLast() =>
        Assert.Equal([15f, -15f, 20f, -20f], Turns(new FitSettings(100, 20)), FloatComparer);

    [Theory]
    [InlineData(180)]
    [InlineData(360)]
    public void ATurnLimitOf180OrMoreReachesEveryHeadingOnce(int limit)
    {
        var turns = Turns(new FitSettings(100, limit));

        Assert.Equal(23, turns.Length);
        Assert.Equal(180f, turns[^1], 3);
        Assert.DoesNotContain(-180f, turns);
    }

    [Fact]
    public void ATurnLimitOf0NeverTurns() =>
        Assert.All(Tries(new FitSettings(80, 0), Own), placement => Assert.Equal(0f, placement.Rotation.Z));

    [Fact]
    public void EachSizeIsTriedWithEveryTurnBeforeTheNextSize()
    {
        var tries = Tries(new FitSettings(95, 15), Own);

        Assert.Equal(
            [(1f, 15f), (1f, -15f), (0.95f, 0f), (0.95f, 15f), (0.95f, -15f)],
            tries.Select(placement => (placement.Scale, MathF.Round(float.RadiansToDegrees(placement.Rotation.Z)))));
    }

    [Fact]
    public void AnObjectThatMayNotBeAdjustedIsNotTried() =>
        Assert.Empty(new PlacementTries(new FitSettings(80, 360)).For(Own, adjustable: false));

    [Fact]
    public void ATurnAddsToTheHeadingOfTheObject()
    {
        var heading = Own with { Rotation = new Vector3(0, 0, 1f) };

        var turned = Tries(new FitSettings(100, 15), heading)[0];

        Assert.Equal(1f + float.DegreesToRadians(15), turned.Rotation.Z, 5);
    }

    private static readonly IEqualityComparer<float> FloatComparer = EqualityComparer<float>.Create((first, second) => MathF.Abs(first - second) < 0.0001f, value => 0);

    private static EulerPlacement[] Tries(FitSettings settings, TargetObject target) => [.. new PlacementTries(settings).For(target, adjustable: true)];

    private static float[] Sizes(FitSettings settings) => [.. Tries(settings, Own).Select(placement => placement.Scale)];

    private static float[] Turns(FitSettings settings) => [.. Tries(settings, Own).Select(placement => float.RadiansToDegrees(placement.Rotation.Z))];
}
