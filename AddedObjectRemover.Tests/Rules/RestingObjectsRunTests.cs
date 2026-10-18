using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The also-remove step in one call: its rounds are decided on its own copy of the decisions and proposed, its time
/// covers the rounds alone, its parts stop on their own, and its report-file details are computed through the run's
/// triangles only when report files are written.
/// </summary>
public class RestingObjectsRunTests
{
    private const float TouchDistance = 1f;
    private const float Threshold = 0.5f;
    private const int Table = 0;
    private const int ItemOnTable = 1;
    private const string ClockReading = "clock";
    private const string ObjectsOfAnyPluginAsk = "objects of any plugin";
    private const string MeshUse = "mesh";
    private const string Failure = "broke";

    private static readonly TestStatic TableModel = new(
        new FormKey(TestTargets.TargetMod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic ItemModel = new(
        new FormKey(TestTargets.TargetMod, 0x702), @"test\item.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));

    private static readonly TestStatic LampModel = new(
        new FormKey(TestTargets.TargetMod, 0x703), @"test\lamp.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-2, -2, 0), new Vector3(2, 2, 30))));

    private static readonly IBaseObjectShapes Shapes =
        TestShapes.Create(TestTargets.TargetMod, "RestingObjectsRunData", TableModel, ItemModel, LampModel);

    /// <summary>A table (the seed) with an item on it, and a lamp far away that no round reaches.</summary>
    private static readonly List<TargetObject> Targets =
    [
        TestTargets.Create(Table, TestTargets.At(Vector3.Zero), TableModel.Base, TestTargets.Space),
        TestTargets.Create(ItemOnTable, TestTargets.At(new Vector3(5, 5, 10)), ItemModel.Base, TestTargets.Space),
        TestTargets.Create(2, TestTargets.At(new Vector3(-500, 0, 0)), LampModel.Base, TestTargets.Space),
    ];

    [Fact]
    public void ModeNothingIsSwitchedOffWithoutReadingTheClockTheCachesOrAMesh()
    {
        var probe = new Probe();

        var result = Run(probe, FollowUpRemovalMode.Nothing);

        Assert.Equal(StepOutcome.SwitchedOff, result.Outcome);
        Assert.Empty(result.Rounds);
        Assert.Empty(result.ProposedRounds);
        Assert.Equal(StepOutcome.SwitchedOff, result.ReportFileDetails.Outcome);
        Assert.Empty(probe.Events);
    }

    [Theory]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport)]
    public void TheRoundsAreProposedAndTheRunsDecisionsAreLeftUnchanged(FollowUpRemovalMode mode)
    {
        var afterTooClose = AfterTooClose();

        var result = Run(new Probe(), mode, afterTooClose);
        var applied = afterTooClose.ApplyRounds(RoundKind.AlsoRemove, result.ProposedRounds);

        Assert.Equal(StepOutcome.Performed, result.Outcome);
        Assert.True(result.HadSeeds);
        Assert.NotEmpty(result.Rounds);
        Assert.Single(afterTooClose.Rounds);
        Assert.True(applied.IsRemoved(new TargetId(ItemOnTable)));
        Assert.Equal(result.Rounds.ToArray(), applied.RoundsAfter(afterTooClose).ToArray());
        Assert.Equal(new[] { new Round(1, RoundKind.TooClose) }, result.SeedRounds.ToArray());
    }

    /// <summary>The table is removed in the changed-terrain round and the too-close round removes nothing, so only the first holds a seed.</summary>
    [Theory]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport)]
    public void TheChangedTerrainRoundsRemovalsSeedTheRoundsToo(FollowUpRemovalMode mode)
    {
        var afterTooClose = TestSeededDecisions.Seed(
            ObjectsToKeep.Build(Targets, [], TestTargets.References(Targets.Count), TestBaseFacts.Unresolved), Targets.Count, seeds: [], terrainSeeds: [Table]);

        var result = Run(new Probe(), mode, afterTooClose);
        var applied = afterTooClose.ApplyRounds(RoundKind.AlsoRemove, result.ProposedRounds);

        Assert.True(result.HadSeeds);
        Assert.Equal(new[] { new Round(1, RoundKind.ChangedTerrain), new Round(2, RoundKind.TooClose) }, result.SeedRounds.ToArray());
        Assert.True(applied.IsRemoved(new TargetId(ItemOnTable)));
        Assert.False(applied.IsDecided(new TargetId(2)));
    }

    [Fact]
    public void AnObjectKeptInTheChangedTerrainRoundIsNotRemovedByTheRounds()
    {
        var protection = ObjectsToKeep.Build(
            Targets, [], TestTargets.References(Targets.Count, new Dictionary<int, KeepReason> { [ItemOnTable] = TestKeepReasons.Quest }), TestBaseFacts.Unresolved);
        var afterTooClose = TestSeededDecisions.Seed(protection, Targets.Count, seeds: [], terrainSeeds: [Table, ItemOnTable]);

        var result = Run(new Probe(), FollowUpRemovalMode.EverythingTouchingIt, afterTooClose);

        Assert.True(result.HadSeeds);
        Assert.Empty(result.ProposedRounds.SelectMany(round => round));
        Assert.IsType<Decision.Kept>(afterTooClose.Of(new TargetId(ItemOnTable)));
    }

    [Fact]
    public void TheSupportRoundsTimeStartsAfterTheObjectsOfAnyPluginAreAskedFor()
    {
        var probe = new Probe();

        var result = Run(probe, FollowUpRemovalMode.OnlyWhatLosesItsSupport, reportFiles: StepOutcome.SwitchedOff);

        Assert.Equal([ObjectsOfAnyPluginAsk, ClockReading], probe.Events.Take(2));
        Assert.Equal(ClockReading, probe.Events[^1]);
        Assert.Equal(TimeSpan.FromSeconds(1), result.RoundsTime);
    }

    [Fact]
    public void TheTouchRoundsTimeEndsBeforeTheTouchChainSearch()
    {
        var probe = new Probe();

        var result = Run(probe, FollowUpRemovalMode.EverythingTouchingIt, touchChainsShown: true);

        var end = probe.Events.LastIndexOf(ClockReading);
        Assert.Equal(ClockReading, probe.Events[0]);
        Assert.Contains(MeshUse, probe.Events.Skip(end + 1));
        Assert.NotNull(result.TouchChains);
        Assert.Equal(TimeSpan.FromSeconds(1), result.RoundsTime);
    }

    [Theory]
    [InlineData(StepOutcome.SwitchedOff)]
    [InlineData(StepOutcome.StoppedByUnexpectedError)]
    public void WithoutReportFilesNoDetailsAreComputedAndTheLampMeshIsNotRead(StepOutcome reportFiles)
    {
        var probe = new Probe();

        var result = Run(probe, FollowUpRemovalMode.OnlyWhatLosesItsSupport, reportFiles: reportFiles);

        Assert.Equal(StepOutcome.Performed, result.Outcome);
        Assert.Same(ReportFileDetails.SwitchedOff, result.ReportFileDetails);
        Assert.DoesNotContain(probe.MeshesUsed, IsLampMesh);
    }

    /// <summary>The details come after the rounds, read the lamp's mesh (which only they read) through the given C6 store and are part of the result.</summary>
    [Fact]
    public void WithReportFilesTheDetailsAreComputedAfterTheRoundsThroughTheGivenTriangleStore()
    {
        var withoutFiles = new Probe();
        Run(withoutFiles, FollowUpRemovalMode.OnlyWhatLosesItsSupport, reportFiles: StepOutcome.SwitchedOff);
        var probe = new Probe();

        var result = Run(probe, FollowUpRemovalMode.OnlyWhatLosesItsSupport);

        Assert.Equal(withoutFiles.Events, probe.Events.Take(withoutFiles.Events.Count));
        Assert.NotEmpty(probe.Events.Skip(withoutFiles.Events.Count));
        Assert.All(probe.Events.Skip(withoutFiles.Events.Count), what => Assert.Equal(MeshUse, what));
        Assert.Contains(probe.MeshesUsed, IsLampMesh);
        Assert.Equal(withoutFiles.Triangles.GetStats().Built + 1, probe.Triangles.GetStats().Built);
        Assert.Equal(StepOutcome.Performed, result.ReportFileDetails.Outcome);
        Assert.Contains(result.ReportFileDetails.MeshOrigins!.Value, origin => IsLampMesh(origin.Mesh));
    }

    [Fact]
    public void AFailureOfTheRoundsProposesNothing()
    {
        var afterTooClose = AfterTooClose();
        var probe = new Probe { FailingClockReading = 2 };

        var result = Run(probe, FollowUpRemovalMode.EverythingTouchingIt, afterTooClose, touchChainsShown: true);

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
        Assert.Equal(new PartFailure(RestingObjectsParts.AlsoRemove, $"InvalidOperationException: {Failure}", Subject: null), result.Failure! with { Details = null });
        Assert.StartsWith($"System.InvalidOperationException: {Failure}", result.Failure.Details, StringComparison.Ordinal);
        Assert.Empty(result.ProposedRounds);
        Assert.Empty(result.Rounds);
        Assert.Null(result.TouchChains);
        Assert.Same(ReportFileDetails.StoppedByEarlierFailure, result.ReportFileDetails);
        Assert.Equal(StepOutcome.StoppedByEarlierFailure, result.ReportFileDetails.Outcome);
    }

    [Fact]
    public void AFailureOfTheTouchChainSearchKeepsTheRoundsAndStopsTheDetailsWithoutASecondFailure()
    {
        var probe = new Probe { MeshesFailAfterClockReading = 2 };

        var result = Run(probe, FollowUpRemovalMode.EverythingTouchingIt, touchChainsShown: true);

        Assert.Equal(StepOutcome.Performed, result.Outcome);
        Assert.NotEmpty(result.ProposedRounds);
        Assert.Null(result.TouchChains);
        Assert.Equal(new PartFailure(RestingObjectsParts.TouchChains, $"InvalidOperationException: {Failure}", Subject: null), result.TouchChainsFailure! with { Details = null });
        Assert.Same(ReportFileDetails.StoppedByEarlierFailure, result.ReportFileDetails);
        Assert.Equal(StepOutcome.StoppedByEarlierFailure, result.ReportFileDetails.Outcome);
    }

    [Fact]
    public void RoundsWithoutSeedsGiveDetailsThatArePerformedWithNothingToShow()
    {
        var withoutSeeds = TestSeededDecisions.Seed(ObjectsToKeep.Build(Targets, [], TestTargets.References(Targets.Count), TestBaseFacts.Unresolved), Targets.Count, []);

        var result = Run(new Probe(), FollowUpRemovalMode.EverythingTouchingIt, withoutSeeds);

        Assert.False(result.HadSeeds);
        Assert.Equal(StepOutcome.Performed, result.ReportFileDetails.Outcome);
        Assert.Null(result.ReportFileDetails.Touch);
        Assert.Null(result.ReportFileDetails.MeshOrigins);
        Assert.Null(result.ReportFileDetails.Failure);
    }

    [Fact]
    public void WithReportFilesTheTouchDetailsHoldTheEdges()
    {
        var result = Run(new Probe(), FollowUpRemovalMode.EverythingTouchingIt);

        Assert.Equal(StepOutcome.Performed, result.ReportFileDetails.Outcome);
        Assert.NotEmpty(result.ReportFileDetails.Touch!.Edges);
        Assert.Null(result.ReportFileDetails.MeshOrigins);
    }

    /// <summary>The lamp's mesh is read only by the details, and its use fails.</summary>
    [Fact]
    public void AFailureOfTheReportFileDetailsIsGivenWithThem()
    {
        var probe = new Probe { LampMeshFails = true };

        var result = Run(probe, FollowUpRemovalMode.OnlyWhatLosesItsSupport);
        var details = result.ReportFileDetails;

        Assert.Equal(StepOutcome.Performed, result.Outcome);
        Assert.Equal(StepOutcome.StoppedByUnexpectedError, details.Outcome);
        Assert.Equal(new PartFailure(RestingObjectsParts.ReportFileDetails, $"InvalidOperationException: {Failure}", Subject: null), details.Failure! with { Details = null });
        Assert.Null(details.MeshOrigins);
    }

    private static RemovalDecisions AfterTooClose() =>
        TestSeededDecisions.Seed(ObjectsToKeep.Build(Targets, [], TestTargets.References(Targets.Count), TestBaseFacts.Unresolved), Targets.Count, [Table]);

    /// <param name="touchChainsShown">Report files show the touch chains too, as the settings check sets it.</param>
    private static RestingObjectsResult Run(
        Probe probe,
        FollowUpRemovalMode mode,
        RemovalDecisions? afterTooClose = null,
        bool touchChainsShown = false,
        StepOutcome reportFiles = StepOutcome.Performed)
    {
        var decisions = afterTooClose ?? AfterTooClose();
        var objectsOfAnyPlugin = TestScenes.CreateWithSupportOnlyObjects(Targets, [], Shapes);
        var caches = new TestRunCaches(
            Shapes,
            probe.Watch(new TriangleStore(TestShapes.MeshFilesOf(Shapes).ReadTriangles)),
            TestGround.NoTerrain(),
            new UnresolvedBases(),
            () =>
            {
                probe.Record(ObjectsOfAnyPluginAsk);
                return objectsOfAnyPlugin.VisibleObjectsOfAnyPlugin();
            });
        var input = new RestingObjectsInput(
            [.. Targets],
            ObjectsToKeep.Build(Targets, [], TestTargets.References(Targets.Count), TestBaseFacts.Unresolved),
            decisions,
            new AlsoRemoveSettings(mode, TouchDistance, Threshold, touchChainsShown || reportFiles == StepOutcome.Performed),
            new Execution(1),
            probe.ReadClock,
            reportFiles);
        return RestingObjects.Find(
            input, caches.Bases, caches.Shapes, caches.Triangles, caches.Terrain, caches.VisibleObjectsOfAnyPlugin);
    }

    private static bool IsLampMesh(string meshPath) => meshPath.EndsWith(LampModel.Model, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Records, in order, the clock readings, the asks for the objects of any plugin and the uses of the run's
    /// triangles, and fails them when told to.
    /// </summary>
    private sealed class Probe
    {
        private int _clockReadings;

        public List<string> Events { get; } = [];

        /// <summary>The mesh paths the run's triangles were used for, in order.</summary>
        public List<string> MeshesUsed { get; } = [];

        public ITriangleMeshes Triangles { get; private set; } = null!;

        /// <summary>The clock reading that throws; none when zero.</summary>
        public int FailingClockReading { get; init; }

        /// <summary>Uses of the run's triangles throw once the clock was read this many times; never when null.</summary>
        public int? MeshesFailAfterClockReading { get; init; }

        /// <summary>Uses of the run's triangles for the lamp's mesh throw.</summary>
        public bool LampMeshFails { get; init; }

        public void Record(string what)
        {
            lock (Events) Events.Add(what);
        }

        public TimeSpan ReadClock()
        {
            Record(ClockReading);
            _clockReadings++;
            if (_clockReadings == FailingClockReading) throw new InvalidOperationException(Failure);
            return TimeSpan.FromSeconds(_clockReadings);
        }

        public ITriangleMeshes Watch(ITriangleMeshes inner)
        {
            Triangles = new WatchedTriangles(inner, this);
            return Triangles;
        }

        private sealed class WatchedTriangles(ITriangleMeshes inner, Probe probe) : ITriangleMeshes
        {
            public Lease<MeshTriangleTree> Acquire(string meshPath)
            {
                if (probe._clockReadings >= probe.MeshesFailAfterClockReading) throw new InvalidOperationException(Failure);
                if (probe.LampMeshFails && IsLampMesh(meshPath)) throw new InvalidOperationException(Failure);
                probe.Record(MeshUse);
                lock (probe.Events) probe.MeshesUsed.Add(meshPath);
                return inner.Acquire(meshPath);
            }

            public TriangleStoreStats GetStats() => inner.GetStats();
        }
    }

    private sealed class UnresolvedBases : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) => BaseFacts.Unresolved(baseKey.Record);
    }
}
