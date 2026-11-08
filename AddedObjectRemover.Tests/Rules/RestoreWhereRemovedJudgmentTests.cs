using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using static AddedObjectRemover.Tests.Fixtures.TestOneRoundSteps;
using static AddedObjectRemover.Tests.Rules.RestoreWhereRemovedScenes;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// Which records restoring where the cleaned mod was removed judges and in what shape: targets the removal steps decided, records the
/// other steps took away, invisible and shrunk objects, never-rules, cells, stops, switches and the worker count.
/// </summary>
public sealed class RestoreWhereRemovedJudgmentTests
{
    [Fact]
    public void ATargetTheRemovalStepsDecidedOrWhoseMarkerTheyMovedIsNotJudged()
    {
        ImmutableArray<TargetObject> targets =
        [
            Own(0, new Vector3(100, 0, 0)),
            Own(1, new Vector3(200, 0, 0)) with { Origin = TargetOrigin.Moved },
            Own(2, new Vector3(300, 0, 0)) with { Origin = TargetOrigin.Moved },
        ];
        var final = DecisionsRemoving(targets, 0, 1).AddMoves([new MarkerMove(new TargetId(2), new Vector3(300, 0, 0), new Vector3(310, 0, 0))]);

        var result = Plan(
            targets, final, [ChangedRock(0x900, Vector3.Zero, target: new TargetId(1)), ChangedRock(0x901, Vector3.Zero, target: new TargetId(2))]);

        Assert.Empty(result.Objects);
    }

    /// <summary>A marker move took a target the cleaned mod moved 10 units on; the after-removal world has it standing there now.</summary>
    [Fact]
    public void ACandidateThatIsATargetStandsNowWhereTheRunsMovesLeaveIt()
    {
        var movedTo = new Vector3(310, 0, 0);
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(300, 0, 0)) with { Origin = TargetOrigin.Moved }];
        var final = DecisionsRemoving(targets, 0).AddMoves([new MarkerMove(new TargetId(1), new Vector3(300, 0, 0), movedTo)]);

        var change = ChangedRock(0x900, Vector3.Zero, target: new TargetId(1)) with { Key = targets[1].Key };

        var world = AfterRemovalWorld.Of(TestScenes.CreateWorld(targets, []), final, OtherModsObjectsResult.SwitchedOff, TestTargets.TargetName, [change], Reach);

        Assert.Equal(movedTo, Assert.Single(world.Now).Transform.Position);
    }

    /// <summary>
    /// Two rocks the cleaned mod changed stand now: one is a change in the world without the cleaned mod, one is not (a later
    /// plugin carries the cleaned mod's value), so its copy is a support-only object and causes no removals.
    /// </summary>
    [Fact]
    public void OnlyTheCopiesOfCandidatesThatAreAChangeWithoutTheCleanedModAreCausesTheTooCloseRuleLeavesOut()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0))];
        var changed = TestShapes.Placed(BaseGameMod, 0, Block.Base, new Vector3(500, 0, 0)) with { ChangeWithoutCleanedMod = new ObjectChange(TestTargets.TargetName, BaseGame) };
        var carried = TestShapes.Placed(BaseGameMod, 1, Block.Base, new Vector3(900, 0, 0));
        var collected = TestScenes.CreateWorld(targets, [changed], [carried]);

        var world = AfterRemovalWorld.Of(
            collected, DecisionsRemoving(targets, 0), OtherModsObjectsResult.SwitchedOff, TestTargets.TargetName, [ChangedRock(0x900, Vector3.Zero), ChangedRock(0x901, Vector3.Zero)], Reach);
        var both = new HashSet<int> { 0, 1 };

        Assert.Equal(2, world.CopiesNowOf(both).Count());
        var cause = Assert.Single(world.CauseCopiesNowOf(both));
        Assert.Equal(changed.Key, world.World.Other(cause).Key);
    }

    /// <summary>The cleaned mod moved a vanilla rock into its camp, and a later mod's lantern rests on the rock there, so the rock is kept.</summary>
    [Fact]
    public void AKeptTargetIsNotJudgedSoWhatRestsOnItWhereItStandsIsNotLeftFloating()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(300, 0, 0)) with { Origin = TargetOrigin.Moved }];
        var resting = new Dictionary<int, KeepReason> { [1] = new(KeepKind.OtherModsObjectRestsOnIt) };
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length, resting), TestBaseFacts.Unresolved);
        var final = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [TooClose(0)]);

        var result = Plan(targets, final, [ChangedRock(0x900, Vector3.Zero, target: new TargetId(1))]);

        Assert.Empty(result.Objects);
    }

    /// <summary>The other mods' objects step pulled one changed rock down and removed another.</summary>
    [Fact]
    public void ARecordTheOtherModsObjectsStepTookAwayIsNotJudged()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0))];
        var otherModsObjects = OtherModsObjectsResult.SwitchedOff with
        {
            Outcome = StepOutcome.Performed,
            Objects =
            [
                new OtherModsObjectOutcome(TestShapes.Placed(BaseGameMod, 0, Block.Base, Vector3.Zero), OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 10f, ReferenceRoles.None),
                new OtherModsObjectOutcome(TestShapes.Placed(BaseGameMod, 1, Block.Base, Vector3.Zero), OtherModsObjectFate.Removed, LostShare: 1f, Drop: 0f, ReferenceRoles.None),
            ],
        };

        var result = Plan(
            targets, DecisionsRemoving(targets, 0), [ChangedRock(0x900, Vector3.Zero), ChangedRock(0x901, Vector3.Zero), ChangedRock(0x902, Vector3.Zero)], otherModsObjects: otherModsObjects);

        Assert.Equal([new RecordKey(BaseGame, 0x902)], result.Objects.Select(judged => judged.Change.Key));
    }

    /// <summary>A kept marker of the cleaned mod stands next to its only visible object, which was removed.</summary>
    [Fact]
    public void AnInvisibleObjectOfTheCleanedModDoesNotCount()
    {
        var marker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), TestTargets.Create(1, TestTargets.At(new Vector3(200, 0, 0)), marker, TestTargets.Space)];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((1, 0, AreaVerdict.Cleared), (judged.JudgedPlace!.Objects, judged.JudgedPlace.Standing, judged.JudgedPlace.Verdict));
    }

    /// <summary>The cleaned mod's mist, whose mesh holds only effect shapes, still stands next to its removed block.</summary>
    [Fact]
    public void AStandingObjectWhoseMeshHoldsOnlyEffectsKeepsItsAreaStillStandingByCountAndAddsNoSize()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(-100, 0, 0), Mist)];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal(
            (2, 1, BlockVolume, 0d, AreaVerdict.StillStanding),
            (judged.JudgedPlace!.Objects, judged.JudgedPlace.Standing, judged.JudgedPlace.Size, judged.JudgedPlace.StandingSize, judged.JudgedPlace.Verdict));
        Assert.False(judged.IsRestored);
    }

    /// <summary>The big block was kept shrunk to half its size, so it counts with an eighth of its volume.</summary>
    [Fact]
    public void AnObjectKeptShrunkCountsAtItsShrunkBox()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0), BigBlock), Own(1, new Vector3(-100, 0, 0))];
        var shrunk = DecisionsRemoving(targets, 1).ApplyFits([new ProposedFit(targets[0].Id, TooClose(0).Reason, targets[0].Placement with { Scale = 0.5f })]);

        var judged = Assert.Single(Plan(targets, shrunk, [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((1, BigBlockVolume / 8), (judged.JudgedPlace!.Standing, judged.JudgedPlace.StandingSize));
    }

    [Fact]
    public void TheCandidatesAreJudgedInKeyOrderWhateverOrderTheyComeIn()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(3000, 0, 0))];
        RecordChangedByCleanedMod[] rocks = [.. Enumerable.Range(0, 6).Select(index => ChangedRock(0x900 + (uint)index, new Vector3(index * 600, 0, 0)))];
        CellChangedByCleanedMod[] cells = [.. Enumerable.Range(0, 4).Select(index => ChangedCell(new RecordKey(BaseGame, 0x500 + (uint)index), (index % 2 - 1, index / 2 - 1)))];

        var inOrder = Plan(targets, removed: [0], rocks, cells);
        var reversed = Plan(targets, removed: [0], Enumerable.Reverse(rocks), Enumerable.Reverse(cells));

        Assert.Equal(inOrder.Objects.Select(Describe), reversed.Objects.Select(Describe));
        Assert.Equal(inOrder.Cells.Select(Describe), reversed.Cells.Select(Describe));
        Assert.Equal(rocks.Select(rock => rock.Key), inOrder.Objects.Select(judged => judged.Change.Key));
    }

    [Fact]
    public void ARecordWithNothingThatGoesBackIsNotJudged()
    {
        var leftToLater = ChangedRock(0x900, Vector3.Zero, undo: PlacedParts.None);

        Assert.Empty(Plan([Own(0, new Vector3(100, 0, 0))], removed: [0], [leftToLater]).Objects);
    }

    [Theory]
    [InlineData(true, false, NeverRestoredReason.WinnerLoadsAfterPatch)]
    [InlineData(true, true, NeverRestoredReason.WinnerLoadsAfterPatch)]
    [InlineData(false, true, NeverRestoredReason.LinkedFromCleanedMod)]
    public void AChangeANeverRuleHoldsForStaysInACleanedArea(bool winnerLoadsAfterPatch, bool linkedFromCleanedMod, NeverRestoredReason expected)
    {
        var rock = ChangedRock(0x900, Vector3.Zero, winner: winnerLoadsAfterPatch ? Later : TestTargets.TargetName, linked: linkedFromCleanedMod);

        var judged = Assert.Single(Plan([Own(0, new Vector3(100, 0, 0))], removed: [0], [rock]).Objects);

        Assert.Equal(((NeverRestoredReason?)expected, (AreaShare?)null, false), (judged.Never, judged.JudgedPlace, judged.IsRestored));
    }

    [Fact]
    public void AnExteriorCellIsJudgedAtItsCentreAndOnlyItsUndonePartsAreListed()
    {
        var centre = new Vector3(ExteriorGrid.CellSize / 2, ExteriorGrid.CellSize / 2, 0);
        ImmutableArray<TargetObject> targets = [Own(0, centre), Own(1, Vector3.Zero)];
        var cell = ChangedCell(ExteriorCellKey, (0, 0));

        var judged = Assert.Single(Plan(targets, removed: [0], [], [cell]).Cells);

        Assert.Equal((1, AreaVerdict.Cleared, true), (judged.Area!.Objects, judged.Area.Verdict, judged.IsRestored));
        Assert.Equal([new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.Undo())], judged.Undone.ToList());
    }

    [Fact]
    public void AnInteriorCellIsJudgedOverTheWholeInterior()
    {
        var interior = new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, null, SpaceKind.Interior, null) };
        var cell = ChangedCell(TestTargets.Space, grid: null);

        var judged = Assert.Single(Plan([Own(0, new Vector3(20000, 0, 0))], removed: [], [], [cell], spaces: interior).Cells);

        Assert.Equal((1, AreaVerdict.StillStanding), (judged.Area!.Objects, judged.Area.Verdict));
    }

    [Fact]
    public void ACellALaterPluginWinsOrTheCleanedModLinksToStays()
    {
        var later = ChangedCell(ExteriorCellKey, (0, 0)) with { WinningMod = Later };
        var linked = ChangedCell(new RecordKey(BaseGame, 0x501), (1, 0)) with { LinkedFromCleanedMod = true };

        var result = Plan([Own(0, Vector3.Zero)], removed: [0], [], [later, linked]);

        Assert.Equal(
            [(NeverRestoredReason.WinnerLoadsAfterPatch, false), (NeverRestoredReason.LinkedFromCleanedMod, false)],
            result.Cells.Select(judged => (judged.Never!.Value, judged.IsRestored)));
    }

    [Fact]
    public void AnUnexpectedErrorStopsTheStepWithItsFailureAndJudgesNothing()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(300, 0, 0))];

        var result = Plan(targets, DecisionsRemoving(targets, 0), [ChangedRock(0x900, Vector3.Zero)], tooClose: (_, _, _, _) => throw new PlantedUserDataException("broke"));

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
        Assert.Empty(result.Objects);
        Assert.Empty(result.Cells);
        Assert.Empty(result.NavmeshGroups);
        var failure = Assert.Single(result.Failures);
        Assert.Equal((RestoreWhereRemovedParts.Step, "PlantedUserDataException: broke", (FailureSubject?)null), (failure.Part, failure.Failure, failure.Subject));
        Assert.NotNull(failure.Details);
    }

    /// <summary>The record whose check stopped stays as the cleaned mod changed it, and the failure names it.</summary>
    [Fact]
    public void ARecordWhoseCheckStoppedUnexpectedlyStaysAndIsNamedByAnObjectCheckFailure()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(300, 0, 0))];

        var result = Plan(targets, DecisionsRemoving(targets, 0), [ChangedRock(0x900, Vector3.Zero)], tooClose: (_, _, _, _) => new StoppingRule());

        var judged = Assert.Single(result.Objects);
        Assert.False(judged.IsRestored);
        Assert.Equal("InvalidOperationException: broke", judged.Refused!.Failure);
        var failure = Assert.Single(result.Failures);
        Assert.Equal((RestoreWhereRemovedParts.ObjectCheck, "InvalidOperationException: broke"), (failure.Part, failure.Failure));
        Assert.Equal((judged.Change.Key.ToString(), 0), (failure.Subject!.Key, failure.Subject.Position));
        Assert.Equal(StepOutcome.Performed, result.Outcome);
    }

    [Fact]
    public void EachKindIsJudgedOnlyWithItsSwitchOn()
    {
        ImmutableArray<TargetObject> targets = [Own(0, Vector3.Zero)];
        RecordChangedByCleanedMod[] rocks = [ChangedRock(0x900, Vector3.Zero)];
        CellChangedByCleanedMod[] cells = [ChangedCell(ExteriorCellKey, (0, 0))];

        var rules = GuardRules(workers: 1);
        var decisions = DecisionsRemoving(targets);
        var world = TestScenes.CreateWorld(targets, []);
        var caches = CachesOf(targets);
        var allOff = RestoreSettings with { OtherModsObjects = false, Terrain = false, Cells = false, Navmeshes = false };
        var groupOff = WhereRemovedRestorer.Plan(
            new(
                new(world, OtherModsObjectsResult.SwitchedOff),
                decisions,
                TestClearedAreas.Of(world, decisions, caches, allOff),
                rules.Over(world),
                TestTargetGroups.None,
                new(caches, TestLandPlugin.Flat, Plugins, TestTargets.TargetName),
                new Execution(1),
                allOff,
                () => TimeSpan.Zero));
        var objectsOff = Plan(targets, removed: [0], rocks, cells, RestoreSettings with { OtherModsObjects = false });
        var cellsOff = Plan(targets, removed: [0], rocks, cells, RestoreSettings with { Cells = false });

        Assert.Equal(StepOutcome.SwitchedOff, groupOff.Outcome);
        Assert.Equal((0, 1), (objectsOff.Objects.Length, objectsOff.Cells.Length));
        Assert.Equal((1, 0), (cellsOff.Objects.Length, cellsOff.Cells.Length));
    }

    [Fact]
    public void TheResultsDoNotDependOnTheNumberOfWorkers()
    {
        const int ObjectCount = 400;
        const int RockCount = 200;
        var random = new Random(8);
        ImmutableArray<TargetObject> targets =
        [
            .. Enumerable.Range(0, ObjectCount).Select(index => Own(index, RandomPoint(random), random.Next(5) == 0 ? BigBlock : Block)),
        ];
        int[] removed = [.. Enumerable.Range(0, ObjectCount).Where(_ => random.Next(10) != 0)];
        RecordChangedByCleanedMod[] rocks = [.. Enumerable.Range(0, RockCount).Select(index => ChangedRock(0x900 + (uint)index, RandomPoint(random)))];
        CellChangedByCleanedMod[] cells = [.. Enumerable.Range(0, 4).Select(index => ChangedCell(new RecordKey(BaseGame, 0x500 + (uint)index), (index % 2 - 1, index / 2 - 1)))];

        var one = Plan(targets, removed, rocks, cells, workers: 1);
        var eight = Plan(targets, removed, rocks, cells, workers: 8);

        Assert.Equal(one.Objects.Select(Describe), eight.Objects.Select(Describe));
        Assert.Equal(one.Cells.Select(Describe), eight.Cells.Select(Describe));
        Assert.Contains(one.Objects, judged => judged.IsRestored);
        Assert.Contains(one.Objects, judged => !judged.IsRestored);
    }

    private static string Describe(PlacedChangeJudged judged) => $"{judged.Change.Key} {judged.Never} {judged.JudgedPlace} {judged.WhereItIsNow} {judged.Refused} {judged.IsRestored}";

    private static string Describe(CellChangeJudged judged) => $"{judged.Change.Cell} {string.Join(", ", judged.Undone)} {judged.Never} {judged.Area} {judged.IsRestored}";

    private static Vector3 RandomPoint(Random random) => new((random.NextSingle() * 2 - 1) * 4000, (random.NextSingle() * 2 - 1) * 4000, 0);

    /// <summary>A too-close rule whose check stops unexpectedly.</summary>
    private sealed class StoppingRule : IPlacementRule
    {
        public IPlacementTries StartTries(TargetObject target) => new StoppingTries();

        private sealed class StoppingTries : IPlacementTries
        {
            public PlacementCheck Check(EulerPlacement placement) => new(Clash: null, Failure: "InvalidOperationException: broke");

            public void Dispose()
            {
            }
        }
    }
}
