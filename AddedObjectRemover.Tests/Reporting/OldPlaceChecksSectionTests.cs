using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The line of the judge step: how many old places it checked and what it found, the collisions under it, and the checks' failures.</summary>
public sealed class OldPlaceChecksSectionTests
{
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly RemovalReason Collision = new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize());

    [Fact]
    public void TheLineCountsTheFreeAndTheCollidingOldPlacesAndTheDetailedLogNamesEachCollision()
    {
        var (world, oldPlaceWorld) = Scene();
        var judged = Judged(new OldPlaceCheck(new TargetId(0), Collision: null, Failure: null), new OldPlaceCheck(new TargetId(1), Collision, Failure: null));

        var sections = TestReport.WithoutProblems().JudgeSections(judged, world, oldPlaceWorld);

        Assert.Equal(["old-places", "unexpectedErrors"], sections.Select(section => section.Id));
        Assert.StartsWith(
            "Checked the old places of 2 objects of other mods that Target.esp changed: 1 free, 1 colliding in ", sections[0].Lines[0], StringComparison.Ordinal);
        var collision = Assert.Single(sections[0].Lines.Skip(1));
        Assert.StartsWith($"  Collides at its old place: {RecordNames.Describe(world.Targets[1])} in ", collision, StringComparison.Ordinal);
        Assert.EndsWith($"; too close to {Describe.OtherObject(oldPlaceWorld.OtherModObjects[0])}, changed by Target.esp (new object).", collision, StringComparison.Ordinal);
        Assert.Empty(sections[1].Lines);
    }

    [Fact]
    public void AFailedCheckIsCountedAndItsErrorPrintsUnderTheLineNamingAnOldPlaceObject()
    {
        var (world, oldPlaceWorld) = Scene();
        var failure = new PartFailure(JudgeChangedObjectsParts.OldPlace, "InvalidOperationException: it broke", new FailureSubject("000805:Skyrim.esm", "B_Crate", 0));
        var judged = Judged(new OldPlaceCheck(new TargetId(0), Collision: null, Failure: "InvalidOperationException: it broke")) with { Failures = [failure] };

        var sections = TestReport.WithoutProblems().JudgeSections(judged, world, oldPlaceWorld);

        Assert.StartsWith("Checked the old places of 1 objects of other mods that Target.esp changed: 0 free, 0 colliding, 1 failed in ", sections[0].Lines[0], StringComparison.Ordinal);
        Assert.Equal(
            "  Unexpected error while checking the old places of objects the cleaned mod changed (InvalidOperationException: it broke): "
            + "1 old-place object, e.g. B_Crate [000805:Skyrim.esm]. Each was treated as colliding at its old place.",
            Assert.Single(sections[1].Lines));
    }

    /// <summary>A failure of the old-place caches lands in the shared channel while the judge step runs; the next step to take errors must not word it.</summary>
    [Fact]
    public void AnOldPlaceCacheFailureInTheSharedChannelPrintsUnderTheJudgeStepsLineAndNotUnderTheNextStep()
    {
        var (world, oldPlaceWorld) = Scene();
        var errors = new UnexpectedErrors();
        var report = TestReport.OverErrors(errors);
        errors.Add(new PartFailure(SelectionParts.RemovalCauseSelection, "InvalidOperationException: it broke", new FailureSubject("000810:Other.esp", "Lantern", 2)));

        var judge = report.JudgeSections(Judged(new OldPlaceCheck(new TargetId(0), Collision: null, Failure: null)), world, oldPlaceWorld);
        var next = report.ReplacementSections(Replacements.None(oldPlaceWorld.OtherModObjects.Length), world);

        Assert.Contains("Unexpected error while selecting the objects that can cause removals", Assert.Single(judge[1].Lines), StringComparison.Ordinal);
        Assert.Empty(next[^1].Lines);
    }

    [Fact]
    public void AStepThatCheckedNoOldPlacePrintsNothingNotEvenAnEmptySection()
    {
        var (world, _) = Scene();

        Assert.Empty(TestReport.WithoutProblems().JudgeSections(Judged(), world, oldPlaceWorld: null));
    }

    private static ChangedObjectJudgingResult Judged(params OldPlaceCheck[] checks) => new([], [.. checks], []);

    /// <summary>Two target objects, and the old-place world, whose only object is the cleaned mod's object the second one collides with.</summary>
    private static (CollectedObjects World, CollectedObjects OldPlaceWorld) Scene()
    {
        var targets = TestTargets.CreateMany(2);
        var world = TestScenes.CreateWorld([.. targets], []) with
        {
            Spaces = new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace, "Skyrim") },
        };
        var change = new ObjectChange(new PluginName("Target.esp"), AcceptedFrom: null);
        var oldPlaceWorld = TestScenes.CreateWorld([], [TestShapes.Placed(OtherMod, 0, default, Vector3.Zero) with { Change = change }]);
        return (world, oldPlaceWorld);
    }
}
