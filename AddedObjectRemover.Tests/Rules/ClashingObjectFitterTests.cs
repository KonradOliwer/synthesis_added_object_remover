using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// Scene: target 0 is an object of the target plugin at the origin; target 1 is a moved replacement at New, replacing the
/// placement Base. The rules are scripted by where a placement stands, how large it is and how far it is turned.
/// </summary>
public class ClashingObjectFitterTests
{
    private static readonly Vector3 New = new(100, 0, 0);
    private static readonly Vector3 Base = new(0, 100, 0);
    private static readonly EulerPlacement BasePlacement = new(Base, Vector3.Zero, 1f);
    private static readonly BaseKey Visible = TestVisibility.TaggedBase(ObjectVisibility.Visible);
    private static readonly BaseKey Invisible = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.SoundMarkers));
    private static readonly IBaseObjectShapes Shapes = TestVisibility.OfTaggedBasesOnly();
    private static readonly RemovalReason TooClose = new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize());

    private static readonly FitSettings Defaults = new(80, 360, ShrinkOrTurnObjects.ReplacementsAfterMovingBack);

    [Fact]
    public void AReplacementThatFitsWhereTheTargetPluginPutItIsNotTried()
    {
        var result = Fit(Defaults, Rule(_ => true));

        Assert.Equal((0, 0), (result.Tried, result.Fits.Length));
    }

    [Fact]
    public void AReplacementThatClashesWhereItStandsIsMovedBackWhenItFitsThere()
    {
        var fit = Assert.Single(Fit(Defaults, Rule(placement => placement.Position == Base)).Fits);

        Assert.Equal(new ProposedFit(new TargetId(1), TooClose, FitKind.MovedBack, BasePlacement), fit);
    }

    [Fact]
    public void AReplacementThatFitsBackOnlyShrunkTakesTheLargestSizeThatFits()
    {
        var fit = Assert.Single(Fit(Defaults, Rule(placement => placement.Position == Base && placement.Scale <= 0.9f)).Fits);

        Assert.Equal(FitKind.MovedBackShrunkOrTurned, fit.Kind);
        Assert.Equal(0.9f, fit.To.Scale, 5);
        Assert.Equal(0f, fit.To.Rotation.Z);
    }

    [Fact]
    public void ATurnTakesTheSmallestTurnThatFitsOneWayBeforeTheOther()
    {
        var settings = Defaults with { ShrinkLimitPercent = 100 };

        var fit = Assert.Single(Fit(settings, Rule(placement => placement.Position == Base && MathF.Abs(placement.Rotation.Z) > 0.4f)).Fits);

        Assert.Equal(float.DegreesToRadians(30), fit.To.Rotation.Z, 5);
    }

    [Fact]
    public void AReplacementThatFitsNowhereIsLeftToTheRemovalRounds()
    {
        var result = Fit(Defaults, Rule(_ => false));

        Assert.Equal((1, 0), (result.Tried, result.Fits.Length));
    }

    [Fact]
    public void WithTheDefaultTheReplacementIsNotShrunkWhereTheTargetPluginPutIt()
    {
        var fit = Assert.Single(Fit(Defaults, Rule(placement => placement.Position == Base || placement.Scale < 1f)).Fits);

        Assert.Equal(FitKind.MovedBack, fit.Kind);
    }

    [Fact]
    public void AtBothSpotsTheReplacementIsShrunkWhereTheTargetPluginPutItBeforeMovingBack()
    {
        var settings = Defaults with { Objects = ShrinkOrTurnObjects.ReplacementsAtBothSpots };

        var fit = Assert.Single(Fit(settings, Rule(placement => placement.Position == Base || placement.Scale < 1f)).Fits);

        Assert.Equal(FitKind.ShrunkOrTurned, fit.Kind);
        Assert.Equal(New, fit.To.Position);
    }

    [Theory]
    [InlineData(ShrinkOrTurnObjects.ReplacementsAfterMovingBack, false)]
    [InlineData(ShrinkOrTurnObjects.ReplacementsAtBothSpots, false)]
    [InlineData(ShrinkOrTurnObjects.AllObjects, true)]
    public void OnlyAllObjectsShrinksTheTargetPluginsOwnObjects(ShrinkOrTurnObjects objects, bool fitted)
    {
        var fits = Fit(Defaults with { Objects = objects }, Rule(placement => placement.Position == Base || placement.Scale < 1f)).Fits;

        Assert.Equal(fitted, fits.Any(fit => fit.Target == new TargetId(0)));
    }

    [Fact]
    public void WithNeitherShrinkingNorTurningAllObjectsOnlyMovesReplacementsBack()
    {
        var settings = new FitSettings(100, 0, ShrinkOrTurnObjects.AllObjects);

        var fit = Assert.Single(Fit(settings, Rule(placement => placement.Position == Base || placement.Scale < 1f)).Fits);

        Assert.Equal((new TargetId(1), FitKind.MovedBack), (fit.Target, fit.Kind));
    }

    [Fact]
    public void AReplacementThePatchCannotPutBackIsNotMovedBackSoTheRoundsRemoveIt()
    {
        var targets = Scene();
        targets = targets.SetItem(1, targets[1] with { Replaced = targets[1].Replaced!.Value with { CanMoveBack = false } });

        var result = ClashingObjectFitter.Fit(targets, Unprotected(targets), [Rule(placement => placement.Position == Base)], Shapes, TargetWorkOrder.Of(targets), new Execution(1), Defaults);

        Assert.Equal((0, 0), (result.Tried, result.Fits.Length));
    }

    [Fact]
    public void AnInvisibleReplacementIsOnlyMovedBack()
    {
        var targets = Scene(replacementBase: Invisible);

        var result = ClashingObjectFitter.Fit(targets, Unprotected(targets), [Rule(placement => placement.Scale < 1f)], Shapes, TargetWorkOrder.Of(targets), new Execution(1), Defaults);

        Assert.Equal((1, 0), (result.Tried, result.Fits.Length));
    }

    [Fact]
    public void AnObjectThatIsAlwaysKeptOrLinkedIsNotTried()
    {
        var targets = Scene();
        var protectedReplacement = ObjectsToKeep.Build(targets, [], TestTargets.References(2, new Dictionary<int, KeepReason> { [1] = TestKeepReasons.Quest }), TestBaseFacts.Unresolved);
        var linkedReplacement = ObjectsToKeep.Build(targets, [TestTargets.Link(0, 1)], TestTargets.References(2), TestBaseFacts.Unresolved);

        Assert.Equal(0, FitWith(targets, protectedReplacement).Tried);
        Assert.Equal(0, FitWith(targets, linkedReplacement).Tried);

        FitResult FitWith(ImmutableArray<TargetObject> scene, IObjectsToKeep objectsToKeep) =>
            ClashingObjectFitter.Fit(scene, objectsToKeep, [Rule(_ => false)], Shapes, TargetWorkOrder.Of(scene), new Execution(1), Defaults);
    }

    [Fact]
    public void TheRulesAreAskedInOrderAndEveryOneMustAccept()
    {
        var fit = Assert.Single(Fit(Defaults, Rule(placement => placement.Position == Base), Rule(placement => placement.Scale <= 0.8f)).Fits);

        Assert.Equal(0.8f, fit.To.Scale, 5);
    }

    [Fact]
    public void AFailedCheckAtAPlacementIsReportedOnceAndThatPlacementIsNotUsed()
    {
        var failing = new ScriptedRule(placement => placement.Position == Base ? new PlacementCheck(null, "broken") : new PlacementCheck(TooClose, null));

        var result = Fit(Defaults, failing);

        Assert.Empty(result.Fits);
        var failure = Assert.Single(result.Failures);
        Assert.Equal((FitParts.FitCheck, "broken"), (failure.Part, failure.Failure));
    }

    [Fact]
    public void TheFitsDoNotDependOnTheThreadCount()
    {
        var targets = Enumerable.Range(0, 40)
            .Select(index => TestTargets.Create(index, TestTargets.At(new Vector3(index * 10, 0, 0)), Visible, TestTargets.Space) with
            {
                Replaced = index % 2 == 0 ? new ReplacedVersion(new EulerPlacement(new Vector3(index * 10, 50, 0), Vector3.Zero, 1f), TestTargets.Space, CanMoveBack: true) : null,
            })
            .ToImmutableArray();
        var rule = Rule(placement => placement.Position.Y > 0 && placement.Scale < 1f + placement.Position.X / 1000f - 0.15f);
        var settings = Defaults with { Objects = ShrinkOrTurnObjects.AllObjects };

        var one = ClashingObjectFitter.Fit(targets, Unprotected(targets), [rule], Shapes, TargetWorkOrder.Of(targets), new Execution(1), settings);
        var eight = ClashingObjectFitter.Fit(targets, Unprotected(targets), [rule], Shapes, TargetWorkOrder.Of(targets), new Execution(8), settings);

        Assert.Equal(Describe(one), Describe(eight));

        static string Describe(FitResult result) =>
            $"{result.Tried}: " + string.Join("; ", result.Fits.Select(fit => $"{fit.Target.Index} {fit.Kind} {fit.To.Position} {fit.To.Rotation.Z:R} {fit.To.Scale:R}"));
    }

    private static FitResult Fit(FitSettings settings, params IPlacementRule[] rules)
    {
        var targets = Scene();
        return ClashingObjectFitter.Fit(targets, Unprotected(targets), rules, Shapes, TargetWorkOrder.Of(targets), new Execution(1), settings);
    }

    private static ImmutableArray<TargetObject> Scene(BaseKey? replacementBase = null) =>
    [
        TestTargets.Create(0, TestTargets.At(Vector3.Zero), Visible, TestTargets.Space),
        TestTargets.Create(1, TestTargets.At(New), replacementBase ?? Visible, TestTargets.Space) with { Replaced = new ReplacedVersion(BasePlacement, TestTargets.Space, CanMoveBack: true) },
    ];

    private static IObjectsToKeep Unprotected(ImmutableArray<TargetObject> targets) =>
        ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved);

    /// <summary>A rule that accepts a placement when <paramref name="fits"/> says so and otherwise finds the object too close.</summary>
    private static ScriptedRule Rule(Func<EulerPlacement, bool> fits) => new(placement => new PlacementCheck(fits(placement) ? null : TooClose, null));

    private sealed class ScriptedRule(Func<EulerPlacement, PlacementCheck> check) : IPlacementRule
    {
        public PlacementCheck Check(TargetId target, EulerPlacement placement) => check(placement);
    }
}
