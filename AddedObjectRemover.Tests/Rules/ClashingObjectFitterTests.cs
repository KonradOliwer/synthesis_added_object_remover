using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// Scene: target 0 is an object of the target plugin at the origin; target 1 is a moved replacement at Moved. The rules are
/// scripted by how large a placement is and how far it is turned.
/// </summary>
public class ClashingObjectFitterTests
{
    private static readonly Vector3 Moved = new(100, 0, 0);
    private static readonly BaseKey Visible = TestVisibility.TaggedBase(ObjectVisibility.Visible);
    private static readonly BaseKey Invisible = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.SoundMarkers));
    private static readonly IBaseObjectShapes Shapes = TestVisibility.OfTaggedBasesOnly();
    private static readonly RemovalReason TooClose = new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize());
    private static readonly RemovalReason TerrainChanged = new RemovalReason.TerrainChanged(new PluginName("Other.esp"), TerrainChangeKind.Height);

    private static readonly FitSettings Defaults = new(80, 360);

    /// <summary>A rule that never clashes.</summary>
    private static readonly ScriptedRule NoClash = Rule(_ => true);

    [Fact]
    public void AnObjectThatFitsWhereItStandsIsNotTried()
    {
        var result = Fit(Defaults, onlyTarget: null, Rule(_ => true));

        Assert.Equal((0, 0, 0), (result.Clashing, result.Tried, result.Fits.Length));
    }

    [Fact]
    public void TheTargetPluginsOwnObjectAndAMovedReplacementAreBothFittedWhereTheyStandWithTheLargestSizeThatFits()
    {
        var result = Fit(Defaults, onlyTarget: null, Rule(placement => placement.Scale <= 0.9f));

        Assert.Equal(2, result.Tried);
        Assert.Equal([new TargetId(0), new TargetId(1)], result.Fits.Select(fit => fit.Target));
        Assert.All(result.Fits, fit => Assert.Equal(0.9f, fit.To.Scale, 5));
        Assert.All(result.Fits, fit => Assert.Equal(0f, fit.To.Rotation.Z));
        Assert.Equal([Vector3.Zero, Moved], result.Fits.Select(fit => fit.To.Position));
    }

    [Fact]
    public void ATurnTakesTheSmallestTurnThatFitsOneWayBeforeTheOther()
    {
        var settings = Defaults with { ShrinkLimitPercent = 100 };

        var fit = Assert.Single(Fit(settings, onlyTarget: 1, Rule(placement => MathF.Abs(placement.Rotation.Z) > 0.4f)).Fits);

        Assert.Equal(float.DegreesToRadians(30), fit.To.Rotation.Z, 5);
    }

    [Fact]
    public void AnObjectThatFitsNowhereIsLeftToTheRemovalRounds()
    {
        var result = Fit(Defaults, onlyTarget: null, Rule(_ => false));

        Assert.Equal((2, 0), (result.Tried, result.Fits.Length));
    }

    [Fact]
    public void WithNeitherShrinkingNorTurningNothingIsTried()
    {
        var result = Fit(new FitSettings(100, 0), onlyTarget: null, Rule(placement => placement.Scale < 1f));

        Assert.Equal((0, 0, 0), (result.Clashing, result.Tried, result.Fits.Length));
    }

    [Fact]
    public void AnInvisibleReplacementIsNotTriedSoTheRoundsJudgeIt()
    {
        var targets = Scene(replacementBase: Invisible);

        var result = ClashingObjectFitter.Fit(
            targets, Unprotected(targets), OneGroup(targets, Defaults, Rule(placement => placement.Scale < 1f)), NothingRestsOnAnything(), Shapes, TargetWorkOrder.Of(targets), new Execution(1));

        Assert.Equal((1, new TargetId(0)), (result.Tried, Assert.Single(result.Fits).Target));
    }

    [Fact]
    public void AnObjectThatIsAlwaysKeptOrLinkedIsNotTried()
    {
        var targets = Scene();
        var protectedReplacement = ObjectsToKeep.Build(targets, [], TestTargets.References(2, new Dictionary<int, KeepReason> { [1] = TestKeepReasons.Quest }), TestBaseFacts.Unresolved);
        var linkedReplacement = ObjectsToKeep.Build(targets, [TestTargets.Link(0, 1)], TestTargets.References(2), TestBaseFacts.Unresolved);

        Assert.Equal(1, FitWith(targets, protectedReplacement).Tried);
        Assert.Equal(0, FitWith(targets, linkedReplacement).Tried);

        FitResult FitWith(ImmutableArray<TargetObject> scene, IObjectsToKeep objectsToKeep) =>
            ClashingObjectFitter.Fit(scene, objectsToKeep, OneGroup(scene, Defaults, Rule(_ => false)), NothingRestsOnAnything(), Shapes, TargetWorkOrder.Of(scene), new Execution(1));
    }

    [Fact]
    public void AnObjectThatClashesByTheUnfittableRuleWhereItStandsIsNotTried()
    {
        var onChangedTerrain = new ScriptedRule(_ => new PlacementCheck(TerrainChanged, null));

        var result = Fit(Defaults, onlyTarget: 1, Rule(placement => placement.Scale <= 0.9f), onChangedTerrain);

        Assert.Equal((0, 0, 0), (result.Clashing, result.Tried, result.Fits.Length));
    }

    [Fact]
    public void EveryTryMustAlsoPassTheUnfittableRule()
    {
        var turnedOntoChangedTerrain = new ScriptedRule(placement => new PlacementCheck(placement.Rotation.Z != 0f ? TerrainChanged : null, null));

        var fit = Assert.Single(Fit(Defaults, onlyTarget: 1, Rule(placement => placement.Scale <= 0.9f || placement.Rotation.Z != 0f), turnedOntoChangedTerrain).Fits);

        Assert.Equal((0.9f, 0f), (fit.To.Scale, fit.To.Rotation.Z));
    }

    [Fact]
    public void AnObjectOtherObjectsRestOnIsNotTriedSoTheRoundsRemoveIt()
    {
        var targets = Scene();
        var resting = new ScriptedResting(_ => [new TargetId(1)]);

        var result = ClashingObjectFitter.Fit(
            targets, Unprotected(targets), OneGroup(targets, Defaults, Rule(placement => placement.Scale <= 0.9f)), resting, Shapes, TargetWorkOrder.Of(targets), new Execution(1));

        Assert.Equal((2, 1, 1), (result.Clashing, result.RestedOn, result.Tried));
        Assert.Equal(new TargetId(0), Assert.Single(result.Fits).Target);
        Assert.Equal([new TargetId(0), new TargetId(1)], resting.Asked);
    }

    [Fact]
    public void WhenFindingTheObjectsOthersRestOnFailsNoObjectIsTried()
    {
        var targets = Scene();
        var failing = new ScriptedResting(_ => throw new InvalidOperationException("broken"));

        var result = ClashingObjectFitter.Fit(
            targets, Unprotected(targets), OneGroup(targets, Defaults, Rule(placement => placement.Scale <= 0.9f)), failing, Shapes, TargetWorkOrder.Of(targets), new Execution(1));

        Assert.Equal((2, 0, 0, 0), (result.Clashing, result.RestedOn, result.Tried, result.Fits.Length));
        Assert.Equal(FitParts.RestingCheck, Assert.Single(result.Failures).Part);
    }

    [Fact]
    public void AFailedCheckAtAPlacementIsReportedOnceAndThatPlacementIsNotUsed()
    {
        var failing = new ScriptedRule(placement => placement.Scale < 1f ? new PlacementCheck(null, "broken") : new PlacementCheck(TooClose, null));

        var result = Fit(Defaults, onlyTarget: 1, failing);

        Assert.Empty(result.Fits);
        var failure = Assert.Single(result.Failures);
        Assert.Equal((FitParts.FitCheck, "broken"), (failure.Part, failure.Failure));
    }

    /// <summary>On one thread at most one object's tries of each rule are open at a time, and none is left open.</summary>
    [Fact]
    public void EachObjectsTriesAreEndedBeforeTheNextObjectIsTried()
    {
        var fittable = new CountingRule(new ScriptedRule(placement => placement.Scale is > 0.92f and < 0.99f
            ? new PlacementCheck(null, "broken")
            : new PlacementCheck(placement.Scale <= 0.9f ? null : TooClose, null)));
        var unfittable = new CountingRule(NoClash);

        var result = Fit(Defaults, onlyTarget: null, fittable, unfittable);

        Assert.Equal((2, 2, 2), (result.Tried, result.Fits.Length, result.Failures.Length));
        Assert.All([fittable, unfittable], rule => Assert.Equal((0, 1), (rule.Open, rule.MostOpen)));
        Assert.True(fittable.Started > result.Tried);
    }

    [Fact]
    public void EachGroupIsTriedWithItsOwnSettingsAndRules()
    {
        var targets = Scene();
        FitGroup[] groups =
        [
            new(TestJudgedGroups.Of([0], Defaults), Rule(placement => placement.Scale <= 0.9f), NoClash),
            new(TestJudgedGroups.Of([1], new FitSettings(40, 360)), Rule(placement => placement.Scale <= 0.6f), NoClash),
        ];

        var result = ClashingObjectFitter.Fit(targets, Unprotected(targets), groups, NothingRestsOnAnything(), Shapes, TargetWorkOrder.Of(targets), new Execution(1));

        Assert.Equal([(0, 0.9f), (1, 0.6f)], result.Fits.Select(fit => (fit.Target.Index, fit.To.Scale)));
    }

    [Fact]
    public void AGroupLeavesTheObjectsOfOtherGroupsAlone()
    {
        var targets = Scene();
        FitGroup[] groups = [new(TestJudgedGroups.Of([1], Defaults), Rule(placement => placement.Scale <= 0.9f), NoClash)];

        var result = ClashingObjectFitter.Fit(targets, Unprotected(targets), groups, NothingRestsOnAnything(), Shapes, TargetWorkOrder.Of(targets), new Execution(1));

        Assert.Equal(new TargetId(1), Assert.Single(result.Fits).Target);
    }

    [Fact]
    public void AnObjectInTwoGroupsIsRefused()
    {
        var targets = Scene();
        FitGroup[] groups =
        [
            new(TestJudgedGroups.Of([0, 1], Defaults), Rule(placement => placement.Scale <= 0.9f), NoClash),
            new(TestJudgedGroups.Of([1], new FitSettings(40, 360)), Rule(placement => placement.Scale <= 0.6f), NoClash),
        ];

        Assert.Throws<System.Diagnostics.UnreachableException>(
            () => ClashingObjectFitter.Fit(targets, Unprotected(targets), groups, NothingRestsOnAnything(), Shapes, TargetWorkOrder.Of(targets), new Execution(1)));
    }

    [Fact]
    public void TheFitsDoNotDependOnTheThreadCount()
    {
        var targets = Enumerable.Range(0, 40)
            .Select(index => TestTargets.Create(index, TestTargets.At(new Vector3(index * 10, 0, 0)), Visible, TestTargets.Space) with { Origin = index % 2 == 0 ? TargetOrigin.Moved : TargetOrigin.CleanedMod })
            .ToImmutableArray();
        var groups = OneGroup(targets, Defaults, Rule(placement => placement.Scale < 1f + placement.Position.X / 1000f - 0.15f));
        var resting = new ScriptedResting(asked => [.. asked.Where(target => target.Index % 7 == 0)]);

        var one = ClashingObjectFitter.Fit(targets, Unprotected(targets), groups, resting, Shapes, TargetWorkOrder.Of(targets), new Execution(1));
        var eight = ClashingObjectFitter.Fit(targets, Unprotected(targets), groups, resting, Shapes, TargetWorkOrder.Of(targets), new Execution(8));

        Assert.Equal(Describe(one), Describe(eight));

        static string Describe(FitResult result) =>
            $"{result.Clashing} {result.RestedOn} {result.Tried}: "
            + string.Join("; ", result.Fits.Select(fit => $"{fit.Target.Index} {fit.To.Position} {fit.To.Rotation.Z:R} {fit.To.Scale:R}"));
    }

    /// <param name="onlyTarget">When given, the other target of the scene is protected, so only this one is tried.</param>
    /// <param name="unfittable">Null: a rule that never clashes.</param>
    private static FitResult Fit(FitSettings settings, int? onlyTarget, IPlacementRule fittable, IPlacementRule? unfittable = null)
    {
        var targets = Scene();
        var objectsToKeep = onlyTarget is { } tried
            ? ObjectsToKeep.Build(targets, [], TestTargets.References(2, new Dictionary<int, KeepReason> { [1 - tried] = TestKeepReasons.Quest }), TestBaseFacts.Unresolved)
            : Unprotected(targets);
        return ClashingObjectFitter.Fit(
            targets, objectsToKeep, OneGroup(targets, settings, fittable, unfittable), NothingRestsOnAnything(), Shapes, TargetWorkOrder.Of(targets), new Execution(1));
    }

    private static ImmutableArray<FitGroup> OneGroup(ImmutableArray<TargetObject> targets, FitSettings settings, IPlacementRule fittable, IPlacementRule? unfittable = null) =>
        [new FitGroup(TestJudgedGroups.OfAll(targets, settings), fittable, unfittable ?? NoClash)];

    private static ScriptedResting NothingRestsOnAnything() => new(_ => []);

    private static ImmutableArray<TargetObject> Scene(BaseKey? replacementBase = null) =>
    [
        TestTargets.Create(0, TestTargets.At(Vector3.Zero), Visible, TestTargets.Space),
        TestTargets.Create(1, TestTargets.At(Moved), replacementBase ?? Visible, TestTargets.Space) with { Origin = TargetOrigin.Moved },
    ];

    private static IObjectsToKeep Unprotected(ImmutableArray<TargetObject> targets) =>
        ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved);

    /// <summary>A rule that accepts a placement when <paramref name="fits"/> says so and otherwise finds the object too close.</summary>
    private static ScriptedRule Rule(Func<EulerPlacement, bool> fits) => new(placement => new PlacementCheck(fits(placement) ? null : TooClose, null));

    private sealed class ScriptedRule(Func<EulerPlacement, PlacementCheck> check) : IPlacementRule
    {
        public IPlacementTries StartTries(TargetObject target) => new ScriptedTries(check);
    }

    private sealed class ScriptedTries(Func<EulerPlacement, PlacementCheck> check) : IPlacementTries
    {
        public PlacementCheck Check(EulerPlacement placement) => check(placement);

        public void Dispose()
        {
        }
    }

    /// <summary>Counts the tries started, those not yet ended, and the most open at once; for one thread.</summary>
    private sealed class CountingRule(IPlacementRule rule) : IPlacementRule
    {
        public int Started { get; private set; }

        public int Open { get; private set; }

        public int MostOpen { get; private set; }

        public IPlacementTries StartTries(TargetObject target)
        {
            Started++;
            Open++;
            MostOpen = Math.Max(MostOpen, Open);
            return new CountedTries(rule.StartTries(target), () => Open--);
        }
    }

    private sealed class CountedTries(IPlacementTries tries, Action ended) : IPlacementTries
    {
        public PlacementCheck Check(EulerPlacement placement) => tries.Check(placement);

        public void Dispose()
        {
            tries.Dispose();
            ended();
        }
    }

    /// <param name="restedOn">Which of the asked objects others rest on.</param>
    private sealed class ScriptedResting(Func<IReadOnlyList<TargetId>, ImmutableArray<TargetId>> restedOn) : IRestingRule
    {
        /// <summary>The objects of the last ask.</summary>
        public IReadOnlyList<TargetId> Asked { get; private set; } = [];

        public ImmutableArray<TargetId> FindObjectsOthersRestOn(IReadOnlyList<TargetId> objects)
        {
            Asked = objects;
            return restedOn(objects);
        }
    }
}
