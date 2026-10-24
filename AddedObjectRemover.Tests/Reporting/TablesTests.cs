using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The report tables, written by <see cref="CsvReportOutput"/>, have the file names, headers and formatting of the checked-in expected-output files.</summary>
public sealed class TablesTests : IDisposable
{
    private const float TouchDistance = 1f;
    private const float Threshold = 0.5f;

    private static readonly KeepReason QuestReason = TestKeepReasons.Quest;

    private static readonly TestStatic TableModel = new(
        new FormKey(TestTargets.TargetMod, 0x711), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic ItemModel = new(
        new FormKey(TestTargets.TargetMod, 0x712), @"test\item,1.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));

    private static readonly TestStatic PlankModel = new(
        new FormKey(TestTargets.TargetMod, 0x713), @"test\plank.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-30.5f, -2, 0), new Vector3(30.5f, 2, 4))));

    private static readonly IBaseObjectShapes Shapes =
        TestShapes.Create(TestTargets.TargetMod, "TablesData", TableModel, ItemModel, PlankModel);

    private static readonly IBaseFacts Bases = new FakeBases(unresolved: ItemModel.FormKey);

    private static readonly ReportContext Context = new(Bases, Shapes, Detailed: false);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "aor-tables-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TouchTables_MatchTheirExpectedOutputFile()
    {
        var scene = TouchScene();
        var settings = new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouchingIt, TouchDistance, Threshold, TouchChainsShown: true);
        var tables = Tables.BuildTables(WithDetails(scene.Outcome, TouchDetails(scene)), settings, Context);

        Assert.NotEmpty(scene.Run.Explanation!.Edges);
        Assert.Single(scene.Run.TouchChains.Members);
        Assert.NotEmpty(scene.Run.AlsoRemoveRounds.SelectMany(round => RemovalList.KeptIn(scene.Run.RemovalDecisions, round)));
        AssertMatchesExpectedOutput("tables-edges", ReportFileNames.EdgesFileName, tables);
        AssertMatchesExpectedOutput("tables-components", ReportFileNames.ComponentsFileName, tables);
    }

    [Fact]
    public void TouchTables_TakeTheChangedTerrainRoundsRemovalsAsSeedsToo()
    {
        var settings = new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouchingIt, TouchDistance, Threshold, TouchChainsShown: true);
        var plain = TouchScene();
        var withTerrain = TouchScene(terrainSeeds: [0]);

        var plainRow = ComponentRow(Tables.BuildTables(WithDetails(plain.Outcome, TouchDetails(plain)), settings, Context));
        var terrainRow = ComponentRow(Tables.BuildTables(WithDetails(withTerrain.Outcome, TouchDetails(withTerrain)), settings, Context));

        Assert.Equal(
            new[] { new Round(1, RoundKind.ChangedTerrain), new Round(2, RoundKind.TooClose) },
            withTerrain.Outcome.RestingObjects.SeedRounds.ToArray());
        Assert.Equal(plainRow.Take(5), terrainRow.Take(5));
        Assert.Contains("terrain changed by Other.esp", terrainRow[^1]);
        Assert.DoesNotContain("terrain changed by", plainRow[^1]);
    }

    [Fact]
    public void SupportTable_MatchesTheOldWriterByteForByte()
    {
        var world = CreateWorld(CreateTargets(), out _);
        var evaluations = new[]
        {
            Evaluation(2, iteration: 2, removed: true),
            Evaluation(1, iteration: 1, removedAsLinked: true),
            Evaluation(0, iteration: 1, held: true),
            Evaluation(3, iteration: 1),
            Evaluation(4, iteration: 1, noContacts: true),
        };

        var table = Tables.BuildSupportTable(world, evaluations, Threshold, Context);

        AssertMatchesExpectedOutput("tables-anchoring", ReportFileNames.SupportFileName, [table]);
        Assert.Equal(5, table.Rows.Length);
    }

    [Fact]
    public void MeshOriginTable_MatchesTheOldWriterByteForByte()
    {
        MeshOrigin[] origins =
        [
            new(@"test\table.nif", ["Table", "Other, table"], 3, new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10)), new Vector3(0.5f, 0.5f, 0f), MeshOriginPlace.NearBottom),
            new(@"test\flat""quoted"".nif", [], 1, new Box(Vector3.Zero, new Vector3(4, 0, 4)), new Vector3(0f, float.NaN, 1.25f), MeshOriginPlace.Other),
        ];

        AssertMatchesExpectedOutput("tables-mesh-origins", ReportFileNames.MeshOriginsFileName, [Tables.BuildMeshOriginsTable(origins)]);
    }

    [Fact]
    public void LeftBehindTable_MatchesTheOldWriterByteForByte()
    {
        var world = CreateWorld(CreateTargets(), out var otherModObject);
        var tally = new SectorAreaTally(50);
        tally.Add(DirectionSector.East, 100f, removed: true);
        tally.Add(DirectionSector.West, 40.5f, removed: false);
        var surroundings = tally.Build();
        LeftBehindCheck[] evaluations =
        [
            new(3, InvisibleObjectKind.DoorMarkers, 250f, otherModObject, surroundings, LeftBehindOutcome.RemovedInsideOtherObject, KeepReason: null),
            new(1, InvisibleObjectKind.IdleMarkers, 100.5f, null, new SectorAreaTally(50).Build(), LeftBehindOutcome.KeptReferenced, QuestReason),
        ];
        var markerMoves = new MarkerMoves(
            StepOutcome.Performed,
            [new MarkerMove(new TargetId(evaluations[0].TargetIndex), new Vector3(1, 2, 3), new Vector3(4, 5, 9))],
            [new MarkerMoveDetail(evaluations[0], RelocationSurface.Terrain, LeftHomeCell: false)],
            [],
            MarkerMoveLimits.MaxDistance,
            Failure: null);

        var table = Tables.BuildLeftBehindTable(world, evaluations, markerMoves, Context);

        AssertMatchesExpectedOutput("tables-leftovers", ReportFileNames.LeftBehindFileName, [table]);
        Assert.Equal(2, table.Rows.Length);
    }

    [Fact]
    public void HintsTable_MatchesTheOldWriterByteForByte()
    {
        var world = CreateWorld(CreateTargets(), out _);
        ManualPatchHint[] hints =
        [
            new(ManualPatchHintType.RemovedMarker, 4, "DoorMarkers"),
            new(ManualPatchHintType.KeptLinkedGroup, 1, "with \"a\", b"),
        ];

        AssertMatchesExpectedOutput("tables-hints", ReportFileNames.HintsFileName, [Tables.BuildHintsTable(world, hints)]);
    }

    [Fact]
    public void RemovalsTable_IsOrderedByWorldspaceHoldPlaceThenKeyAndMatchesTheExpectedOutput()
    {
        var targets = CreateTargets();
        targets[0] = targets[0] with { Area = new TargetArea("The Pale", "Loreius Farm", PlaceSource.OwnLocation, new CellGridPoint(7, 6)) };
        targets[1] = targets[1] with { Area = new TargetArea("The Pale", "Dawnstar", PlaceSource.NearbyLocation, new CellGridPoint(8, -6)) };
        targets[2] = targets[2] with { Area = new TargetArea(null, null, PlaceSource.GridOnly, new CellGridPoint(0, 0)) };
        targets[4] = targets[4] with { Area = new TargetArea("The Pale", "Loreius Farm", PlaceSource.OwnLocation, new CellGridPoint(7, 6)) };
        var world = CreateWorld(targets, out var otherModObject);
        RemovedObject[] removals =
        [
            new TooCloseRemoval(4, otherModObject, new TooCloseCause.ObjectSize()),
            new TouchingRemoval(2, 4),
            new LinkedRemoval(1, 4),
            new TooCloseRemoval(0, otherModObject, new TooCloseCause.ObjectSize()),
        ];

        AssertMatchesExpectedOutput("tables-removals", ReportFileNames.RemovalsFileName, [Tables.BuildRemovalsTable(world, removals, Context)]);
    }

    [Fact]
    public void Build_TouchExplanationsGiveEdgesComponentsAndHints()
    {
        var scene = TouchScene();
        var settings = new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouchingIt, TouchDistance, Threshold, TouchChainsShown: true);

        var tables = Tables.BuildTables(WithDetails(scene.Outcome, TouchDetails(scene)), settings, Context);

        Assert.Equal(
            [ReportFileNames.EdgesFileName, ReportFileNames.ComponentsFileName, ReportFileNames.RemovalsFileName, ReportFileNames.HintsFileName],
            tables.Select(table => table.FileName));
    }

    [Theory]
    [InlineData(StepOutcome.SwitchedOff)]
    [InlineData(StepOutcome.StoppedByUnexpectedError)]
    public void Build_WithoutPerformedDetailsLeftBehindOrSupportRoundsGivesOnlyRemovalsAndHints(StepOutcome detailsOutcome)
    {
        var scene = TouchScene();
        var settings = new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouchingIt, TouchDistance, Threshold, TouchChainsShown: true);
        var details = ReportFileDetails.SwitchedOff with { Outcome = detailsOutcome };

        var tables = Tables.BuildTables(WithDetails(scene.Outcome, details), settings, Context);

        Assert.Equal([ReportFileNames.RemovalsFileName, ReportFileNames.HintsFileName], tables.Select(table => table.FileName));
    }

    [Fact]
    public void Build_PerformedTouchDetailsWithoutTheEdgesAreABug()
    {
        var scene = TouchScene();
        var settings = new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouchingIt, TouchDistance, Threshold, TouchChainsShown: true);

        Assert.Throws<InvalidOperationException>(() => Tables.BuildTables(WithDetails(scene.Outcome, ReportFileDetails.None), settings, Context));
    }

    [Fact]
    public void Build_PerformedMeshOriginDetailsWithoutTheOriginsAreABug()
    {
        var scene = TouchScene();
        var settings = new AlsoRemoveSettings(FollowUpRemovalMode.OnlyWhatLosesItsSupport, TouchDistance, Threshold, TouchChainsShown: true);
        var outcome = scene.Outcome with { RestingObjects = scene.Outcome.RestingObjects with { Mode = settings.Mode, Rounds = [], Evidence = [] } };

        Assert.Throws<InvalidOperationException>(() => Tables.BuildTables(WithDetails(outcome, ReportFileDetails.None), settings, Context));
    }

    [Theory]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport, true, true)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport, false, false)]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt, true, false)]
    [InlineData(FollowUpRemovalMode.Nothing, true, false)]
    public void Build_WritesSupportOnlyForSupportRoundsWithSeeds(FollowUpRemovalMode mode, bool hadSeeds, bool expected)
    {
        var scene = TouchScene();
        var alsoRemove = scene.Outcome.RestingObjects with { Mode = mode, HadSeeds = hadSeeds, Rounds = [], Evidence = [] };
        var outcome = scene.Outcome with { RestingObjects = alsoRemove };

        var tables = Tables.BuildTables(WithDetails(outcome, ReportFileDetails.SwitchedOff), new AlsoRemoveSettings(mode, TouchDistance, Threshold, TouchChainsShown: true), Context);

        Assert.Equal(expected, tables.Any(table => table.FileName == ReportFileNames.SupportFileName));
    }

    [Theory]
    [InlineData(StepOutcome.StoppedByUnexpectedError)]
    [InlineData(StepOutcome.SwitchedOff)]
    public void Build_WritesNoSupportTableForAStepThatDidNotRun(StepOutcome outcome)
    {
        var scene = TouchScene();
        var settings = new AlsoRemoveSettings(FollowUpRemovalMode.OnlyWhatLosesItsSupport, TouchDistance, Threshold, TouchChainsShown: true);
        var alsoRemove = scene.Outcome.RestingObjects with { Outcome = outcome, Mode = settings.Mode, HadSeeds = false, Rounds = [], Evidence = [] };

        var tables = Tables.BuildTables(WithDetails(scene.Outcome with { RestingObjects = alsoRemove }, ReportFileDetails.SwitchedOff), settings, Context);

        Assert.DoesNotContain(tables, table => table.FileName == ReportFileNames.SupportFileName);
    }

    [Fact]
    public void Build_AStoppedLeftBehindStepStillGetsItsHeaderOnlyTable()
    {
        var scene = TouchScene();
        var settings = new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouchingIt, TouchDistance, Threshold, TouchChainsShown: true);
        var stopped = new LeftBehindResult(StepOutcome.StoppedByUnexpectedError, [], [], new PartFailure(LeftBehindParts.LeftBehind, "it broke", Subject: null));

        var tables = Tables.BuildTables(WithDetails(scene.Outcome with { LeftBehind = stopped }, ReportFileDetails.SwitchedOff), settings, Context);

        Assert.Empty(tables.Single(table => table.FileName == ReportFileNames.LeftBehindFileName).Rows);
    }

    [Fact]
    public void Build_ListsTheFilesInTheOrderTheStepsRan()
    {
        var scene = TouchScene();
        var settings = new AlsoRemoveSettings(FollowUpRemovalMode.OnlyWhatLosesItsSupport, TouchDistance, Threshold, TouchChainsShown: true);
        var outcome = scene.Outcome with
        {
            RestingObjects = scene.Outcome.RestingObjects with { Mode = settings.Mode, Rounds = [], Evidence = [] },
            LeftBehind = TestLeftBehind.PerformedWithoutFindings,
        };
        var origins = new ReportFileDetails(
            StepOutcome.Performed, null, [new MeshOrigin("m.nif", [], 1, default, Vector3.Zero, MeshOriginPlace.Other)], Failure: null);

        var tables = Tables.BuildTables(WithDetails(outcome, origins), settings, Context);

        Assert.Equal(
            [ReportFileNames.SupportFileName, ReportFileNames.MeshOriginsFileName, ReportFileNames.LeftBehindFileName, ReportFileNames.RemovalsFileName, ReportFileNames.HintsFileName],
            tables.Select(table => table.FileName));
    }

    private static ImmutableArray<string> ComponentRow(ImmutableArray<CsvTable> tables) =>
        tables.Single(table => table.FileName == ReportFileNames.ComponentsFileName).Rows.Single();

    private static RunOutcome WithDetails(RunOutcome outcome, ReportFileDetails details) =>
        outcome with { RestingObjects = outcome.RestingObjects with { ReportFileDetails = details } };

    private static ReportFileDetails TouchDetails(Scene scene) =>
        new(StepOutcome.Performed, scene.Run.Explanation, null, Failure: null);

    private sealed record Scene(RunOutcome Outcome, TestTouchCascade.Run Run);

    /// <summary>Tables 0 and 2 are seeds; the plank 1 and item 4 are removed by touch; the referenced item 3 is held; target 4 has a comma in its Editor ID.</summary>
    /// <param name="terrainSeeds">Seeds removed by the changed-terrain round instead of the too-close round; none by default.</param>
    private static Scene TouchScene(IReadOnlyList<int>? terrainSeeds = null)
    {
        var targets = CreateTargets();
        var protection = ObjectsToKeep.Build(
            [.. targets], [], TestTargets.References(targets.Count, new Dictionary<int, KeepReason> { [3] = QuestReason }), TestBaseFacts.Unresolved);
        var run = TestTouchCascade.Execute(
            targets, Shapes, protection, terrainSeeds is null ? [0, 2] : [2], TouchDistance, threads: 2, collectDiagnostics: true, terrainSeeds);
        var world = CreateWorld(targets, out _);
        var decided = new StepResults(
            world, protection, new RemovalRisks([], []), ChangedTerrainResult.SwitchedOff, run.Result, LeftBehindResult.SwitchedOff, MarkerMoves.SwitchedOff, run.RemovalDecisions);
        var removals = RemovalList.Removals(PatchWriter.ListRemovalsInWriteOrder(run.RemovalDecisions), run.RemovalDecisions, world, TestLeftBehind.PerformedWithoutFindings).ToImmutableArray();
        var outcome = new RunOutcome(decided, removals, ManualPatchHints.FindManualPatchHints(decided, Shapes, removals), null!, default);
        return new Scene(outcome, run);
    }

    private static List<TargetObject> CreateTargets() =>
    [
        Place(0, TableModel, new Vector3(0, 0, 0)),
        Place(1, PlankModel, new Vector3(50, 0, 3)),
        Place(2, TableModel, new Vector3(100, 0, 0)),
        Place(3, ItemModel, new Vector3(5, 5, 10)),
        Place(4, ItemModel, new Vector3(105, 5, 10)) with { EditorId = "Item,\"4\"", Cell = new CellFact(TestTargets.SpaceKey(0x9000), "Cell, one") },
    ];

    private static TargetObject Place(int index, TestStatic model, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position, zRadians: 0.3f * index, scale: 1f + index * 0.25f), model.Base, TestTargets.Space);

    private static CollectedObjects CreateWorld(List<TargetObject> targets, out OtherObject otherModObject)
    {
        otherModObject = new OtherObject(
            new OtherId(0),
            new FormKey(ModKey.FromNameAndExtension("Other.esp"), 0x10).ToRecordKey(),
            TestTargets.Space,
            new PluginName("Other.esp"),
            EditorId: "Rival,1",
            Base: null,
            Vector3.Zero,
            default(Vector3),
            Scale: 1f,
            IsPrimitive: false,
            HasMapMarker: false,
            Change: new ObjectChange(new PluginName("Other.esp"), AcceptedFrom: null));
        return new CollectedObjects(
            [.. targets],
            [otherModObject],
            [],
            false,
            [],
            [],
            new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel, Test", SpaceKind.Worldspace, "Skyrim, Test") },
            new ReadCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            [],
            TargetWorkOrder.Of(targets),
            null);
    }

    private static SupportEvaluationRow Evaluation(
        int target, int iteration, bool removed = false, bool removedAsLinked = false, bool held = false, bool noContacts = false)
    {
        var supporters = noContacts
            ? []
            : new List<SupporterShare>
            {
                new(Supporter.Target(2), SupportCategory.RemovedTarget, 0.5f),
                new(Supporter.Placed(new OtherId(0)), SupportCategory.OtherMod, 0.25f),
                new(Supporter.Terrain, SupportCategory.Terrain, 0.125f),
                new(Supporter.Target(0), SupportCategory.KeptTarget, 0.0625f),
            };
        var contacts = new CandidateContacts(noContacts ? 0 : 12, noContacts ? 0f : 7.5f, []);
        return new SupportEvaluationRow(target, iteration, contacts, supporters, removed, removedAsLinked, held);
    }

    private ReportFileSettings Reports(string name) => new(Folder(name));

    private string Folder(string name) => Path.Combine(_root, name);

    private void AssertMatchesExpectedOutput(string expectedOutputName, string fileName, ImmutableArray<CsvTable> tables)
    {
        var table = tables.Single(candidate => candidate.FileName == fileName);
        var result = new CsvReportOutput().Write(Reports("new"), new ReportTables(StepOutcome.Performed, [table], Failure: null));

        Assert.Empty(result.Warnings);
        ExpectedOutputFiles.AssertMatches(expectedOutputName, File.ReadAllLines(result.Written.Single().Path));
    }

    private sealed class FakeBases(FormKey unresolved) : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) => baseKey.Record == unresolved.ToRecordKey()
            ? BaseFacts.Unresolved(baseKey.Record)
            : new BaseFacts(baseKey.Record, true, BaseRecordKind.Static, "Static", $"Base{baseKey.Record.Id:X}", null, null, null, ImmutableArray<string>.Empty, null, null, null);
    }
}
