using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The placements a clashing object is tried at, in order, for each setting of the shrink limit, the turn limit and the objects tried.</summary>
public class PlacementTriesTests
{
    private static readonly EulerPlacement New = new(new Vector3(100, 0, 0), Vector3.Zero, 1f);
    private static readonly EulerPlacement Base = new(new Vector3(0, 100, 0), new Vector3(0, 0, 1f), 2f);
    private static readonly TargetObject Own = TestTargets.Create(0, New.Transform);
    private static readonly TargetObject Replacement = TestTargets.Create(1, New.Transform) with { Replaced = new ReplacedVersion(Base, TestTargets.Space, CanMoveBack: true) };

    [Fact]
    public void TheDefaultMovesAReplacementBackThenAdjustsItThere()
    {
        var tries = Tries(new FitSettings(90, 0, ShrinkOrTurnObjects.ReplacementsAfterMovingBack), Replacement);

        Assert.Equal([FitKind.MovedBack, FitKind.MovedBackShrunkOrTurned, FitKind.MovedBackShrunkOrTurned], tries.Select(placementTry => placementTry.Kind));
        Assert.Equal([2f, 1.9f, 1.8f], tries.Select(placementTry => placementTry.Placement.Scale), FloatComparer);
        Assert.All(tries, placementTry => Assert.Equal(Base.Position, placementTry.Placement.Position));
    }

    [Fact]
    public void AtBothSpotsAReplacementIsAdjustedWhereItStandsBeforeMovingBack()
    {
        var tries = Tries(new FitSettings(90, 0, ShrinkOrTurnObjects.ReplacementsAtBothSpots), Replacement);

        Assert.Equal(
            [FitKind.ShrunkOrTurned, FitKind.ShrunkOrTurned, FitKind.MovedBack, FitKind.MovedBackShrunkOrTurned, FitKind.MovedBackShrunkOrTurned],
            tries.Select(placementTry => placementTry.Kind));
        Assert.Equal([0.95f, 0.9f], tries.Take(2).Select(placementTry => placementTry.Placement.Scale), FloatComparer);
    }

    [Theory]
    [InlineData(ShrinkOrTurnObjects.ReplacementsAfterMovingBack, 0)]
    [InlineData(ShrinkOrTurnObjects.ReplacementsAtBothSpots, 0)]
    [InlineData(ShrinkOrTurnObjects.AllObjects, 2)]
    public void OnlyAllObjectsTriesTheTargetPluginsOwnObjects(ShrinkOrTurnObjects objects, int count) =>
        Assert.Equal(count, Tries(new FitSettings(90, 0, objects), Own).Length);

    [Fact]
    public void TheShrinkLimitItselfIsTheLastSize() =>
        Assert.Equal([0.95f, 0.9f, 0.85f, 0.83f], Sizes(new FitSettings(83, 0, ShrinkOrTurnObjects.AllObjects)), FloatComparer);

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void AShrinkLimitOf100OrMoreNeverShrinks(int limit) =>
        Assert.Empty(Tries(new FitSettings(limit, 0, ShrinkOrTurnObjects.AllObjects), Own));

    [Fact]
    public void TurnsGoFromTheSmallestUpOneWayBeforeTheOtherTheLimitItselfLast() =>
        Assert.Equal([15f, -15f, 20f, -20f], Turns(new FitSettings(100, 20, ShrinkOrTurnObjects.AllObjects)), FloatComparer);

    [Theory]
    [InlineData(180)]
    [InlineData(360)]
    public void ATurnLimitOf180OrMoreReachesEveryHeadingOnce(int limit)
    {
        var turns = Turns(new FitSettings(100, limit, ShrinkOrTurnObjects.AllObjects));

        Assert.Equal(23, turns.Length);
        Assert.Equal(180f, turns[^1], 3);
        Assert.DoesNotContain(-180f, turns);
    }

    [Fact]
    public void ATurnLimitOf0NeverTurns() =>
        Assert.All(Tries(new FitSettings(80, 0, ShrinkOrTurnObjects.AllObjects), Own), placementTry => Assert.Equal(0f, placementTry.Placement.Rotation.Z));

    [Fact]
    public void EachSizeIsTriedWithEveryTurnBeforeTheNextSize()
    {
        var tries = Tries(new FitSettings(95, 15, ShrinkOrTurnObjects.AllObjects), Own);

        Assert.Equal(
            [(1f, 15f), (1f, -15f), (0.95f, 0f), (0.95f, 15f), (0.95f, -15f)],
            tries.Select(placementTry => (placementTry.Placement.Scale, MathF.Round(float.RadiansToDegrees(placementTry.Placement.Rotation.Z)))));
    }

    [Fact]
    public void AnObjectThatMayNotBeAdjustedIsOnlyMovedBack()
    {
        var placementTry = Assert.Single(new PlacementTries(new FitSettings(80, 360, ShrinkOrTurnObjects.AllObjects)).For(Replacement, adjustable: false));

        Assert.Equal(new PlacementTry(FitKind.MovedBack, Base), placementTry);
    }

    [Theory]
    [InlineData(ShrinkOrTurnObjects.ReplacementsAfterMovingBack, 0)]
    [InlineData(ShrinkOrTurnObjects.ReplacementsAtBothSpots, 2)]
    public void AReplacementThePatchCannotPutBackIsTriedOnlyWhereItStands(ShrinkOrTurnObjects objects, int count)
    {
        var stuck = Replacement with { Replaced = Replacement.Replaced!.Value with { CanMoveBack = false } };

        var tries = Tries(new FitSettings(90, 0, objects), stuck);

        Assert.Equal(count, tries.Length);
        Assert.All(tries, placementTry => Assert.Equal(FitKind.ShrunkOrTurned, placementTry.Kind));
    }

    [Fact]
    public void ATurnAddsToTheHeadingOfThePlacementItAdjusts()
    {
        var turned = Tries(new FitSettings(100, 15, ShrinkOrTurnObjects.ReplacementsAfterMovingBack), Replacement)[1];

        Assert.Equal(1f + float.DegreesToRadians(15), turned.Placement.Rotation.Z, 5);
    }

    private static readonly IEqualityComparer<float> FloatComparer = EqualityComparer<float>.Create((first, second) => MathF.Abs(first - second) < 0.0001f, value => 0);

    private static PlacementTry[] Tries(FitSettings settings, TargetObject target) => [.. new PlacementTries(settings).For(target, adjustable: true)];

    private static float[] Sizes(FitSettings settings) => [.. Tries(settings, Own).Select(placementTry => placementTry.Placement.Scale)];

    private static float[] Turns(FitSettings settings) => [.. Tries(settings, Own).Select(placementTry => float.RadiansToDegrees(placementTry.Placement.Rotation.Z))];
}
