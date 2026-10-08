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
        var options = new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouching, TouchDistance, Threshold);
        var tables = Tables.Build(
            scene.Outcome, new ReportFileDetails(scene.Run.Explanation, null), Reports("new"), null, options, Context);

        Assert.NotEmpty(scene.Run.Explanation!.Edges);
        Assert.Single(scene.Run.TouchChains.Members);
        Assert.NotEmpty(scene.Run.AlsoRemoveRounds.SelectMany(round => RemovalList.KeptIn(scene.Run.RemovalDecisions, round)));
        AssertMatchesExpectedOutput("tables-edges", ReportFileNames.EdgesFileName, tables);
        AssertMatchesExpectedOutput("tables-components", ReportFileNames.ComponentsFileName, tables);
    }

    [Fact]
    public void AnchoringTable_MatchesTheOldWriterByteForByte()
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

        var table = Tables.Anchoring(world, evaluations, Threshold, Context);

        AssertMatchesExpectedOutput("tables-anchoring", ReportFileNames.AnchoringFileName, [table]);
        Assert.Equal(5, table.Rows.Length);
    }

    [Fact]
    public void MeshOriginTable_MatchesTheOldWriterByteForByte()
    {
        MeshOrigin[] origins =
        [
            new(@"test\table.nif", ["Table", "Other, table"], 3, new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10)), new Vector3(0.5f, 0.5f, 0f), "near bottom"),
            new(@"test\flat""quoted"".nif", [], 1, new Box(Vector3.Zero, new Vector3(4, 0, 4)), new Vector3(0f, float.NaN, 1.25f), "other"),
        ];

        AssertMatchesExpectedOutput("tables-mesh-origins", ReportFileNames.MeshOriginsFileName, [Tables.MeshOrigins(origins)]);
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
            [new KeptMarkerMove(evaluations[0], new Vector3(1, 2, 3), new Vector3(4, 5, 9), RelocationSurface.Terrain, LeftHomeCell: false)], []);

        var table = Tables.LeftBehind(world, evaluations, markerMoves, Context);

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

        AssertMatchesExpectedOutput("tables-hints", ReportFileNames.HintsFileName, [Tables.Hints(world, hints)]);
    }

    [Fact]
    public void Build_WritesNothingWhenReportFilesAreOff()
    {
        var scene = TouchScene();
        var options = new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouching, TouchDistance, Threshold);

        var tables = Tables.Build(
            scene.Outcome, new ReportFileDetails(scene.Run.Explanation, null), new ReportOptions(false, Folder("off")), LeftBehindOptionsOn(), options, Context);

        Assert.Empty(tables);
    }

    [Fact]
    public void Build_TouchExplanationsGiveEdgesComponentsAndHints()
    {
        var scene = TouchScene();
        var options = new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouching, TouchDistance, Threshold);

        var tables = Tables.Build(scene.Outcome, new ReportFileDetails(scene.Run.Explanation, null), Reports("new"), null, options, Context);

        Assert.Equal(
            [ReportFileNames.EdgesFileName, ReportFileNames.ComponentsFileName, ReportFileNames.HintsFileName], tables.Select(table => table.FileName));
    }

    [Fact]
    public void Build_WithoutExplanationsLeftBehindOrSupportRoundsGivesOnlyHints()
    {
        var scene = TouchScene();
        var options = new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouching, TouchDistance, Threshold);

        var tables = Tables.Build(scene.Outcome, ReportFileDetails.None, Reports("new"), null, options, Context);

        Assert.Equal([ReportFileNames.HintsFileName], tables.Select(table => table.FileName));
    }

    [Theory]
    [InlineData(FollowUpRemovalMode.ObjectsSupportedByIt, true, true)]
    [InlineData(FollowUpRemovalMode.ObjectsSupportedByIt, false, false)]
    [InlineData(FollowUpRemovalMode.EverythingTouching, true, false)]
    [InlineData(FollowUpRemovalMode.Nothing, true, false)]
    public void Build_WritesAnchoringOnlyForSupportRoundsWithSeeds(FollowUpRemovalMode mode, bool hadSeeds, bool expected)
    {
        var scene = TouchScene();
        var alsoRemove = scene.Outcome.RestingObjects with { Mode = mode, HadSeeds = hadSeeds, Rounds = [], Evidence = [] };
        var outcome = scene.Outcome with { RestingObjects = alsoRemove };

        var tables = Tables.Build(outcome, ReportFileDetails.None, Reports("new"), null, new AlsoRemoveSettings(mode, TouchDistance, Threshold), Context);

        Assert.Equal(expected, tables.Any(table => table.FileName == ReportFileNames.AnchoringFileName));
    }

    [Fact]
    public void Build_ListsTheFilesInTheOrderTheStepsRan()
    {
        var scene = TouchScene();
        var options = new AlsoRemoveSettings(FollowUpRemovalMode.ObjectsSupportedByIt, TouchDistance, Threshold);
        var outcome = scene.Outcome with { RestingObjects = scene.Outcome.RestingObjects with { Mode = options.Mode, Rounds = [], Evidence = [] } };
        var origins = new ReportFileDetails(null, [new MeshOrigin("m.nif", [], 1, default, Vector3.Zero, "other")]);

        var tables = Tables.Build(outcome, origins, Reports("new"), LeftBehindOptionsOn(), options, Context);

        Assert.Equal(
            [ReportFileNames.AnchoringFileName, ReportFileNames.MeshOriginsFileName, ReportFileNames.LeftBehindFileName, ReportFileNames.HintsFileName],
            tables.Select(table => table.FileName));
    }

    private sealed record Scene(RunOutcome Outcome, TestTouchCascade.Run Run);

    /// <summary>Tables 0 and 2 are seeds; the plank 1 and item 4 are removed by touch; the referenced item 3 is held; target 4 has a comma in its Editor ID.</summary>
    private static Scene TouchScene()
    {
        var targets = CreateTargets();
        var protection = ObjectsToKeep.Build(
            [.. targets], [], TestTargets.References(targets.Count, new Dictionary<int, KeepReason> { [3] = QuestReason }));
        var run = TestTouchCascade.Execute(targets, Shapes, protection, [0, 2], TouchDistance, threads: 2, collectDiagnostics: true);
        var world = CreateWorld(targets, out _);
        var decided = new StepResults(
            world, null!, null!, protection, null!, run.Result, null, LeftBehindResult.None, MarkerMoves.None, run.RemovalDecisions);
        var removals = RemovalList.Removals(run.RemovalDecisions, world, LeftBehindResult.None).ToImmutableArray();
        var outcome = new RunOutcome(decided, removals, ManualPatchHints.Hints(decided, Shapes, removals), null!, [], default);
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
            HasMapMarker: false);
        return new CollectedObjects(
            [.. targets],
            [otherModObject],
            null,
            [],
            new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel, Test", SpaceKind.Worldspace) },
            new ReadCounts(0, 0, 0, 0, 0, 0),
            []);
    }

    private static AnchoringEvaluation Evaluation(
        int target, int iteration, bool removed = false, bool removedAsLinked = false, bool held = false, bool noContacts = false)
    {
        var supporters = noContacts
            ? []
            : new List<SupporterShare>
            {
                new(Supporter.Target(2), SupportCategory.RemovedTarget, 0.5f),
                new(Supporter.Placed(new OtherId(0)), SupportCategory.OtherPlugin, 0.25f),
                new(Supporter.Terrain, SupportCategory.Terrain, 0.125f),
                new(Supporter.Target(0), SupportCategory.KeptTarget, 0.0625f),
            };
        var contacts = new CandidateContacts(noContacts ? 0 : 12, noContacts ? 0f : 7.5f, []);
        return new AnchoringEvaluation(target, iteration, contacts, supporters, removed, removedAsLinked, held);
    }

    private static LeftBehindOptions LeftBehindOptionsOn() => new(100f, 50, 50, 50, new HashSet<InvisibleObjectKind>(), default);

    private ReportOptions Reports(string name) => new(true, Folder(name));

    private string Folder(string name) => Path.Combine(_root, name);

    private void AssertMatchesExpectedOutput(string expectedOutputName, string fileName, ImmutableArray<CsvTable> tables)
    {
        var table = tables.Single(candidate => candidate.FileName == fileName);
        var result = new CsvReportOutput().Write(Reports("new"), [table]);

        Assert.Empty(result.Warnings);
        ExpectedOutputFiles.AssertMatches(expectedOutputName, File.ReadAllLines(result.Written.Single().Path));
    }

    private sealed class FakeBases(FormKey unresolved) : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) => baseKey.Record == unresolved.ToRecordKey()
            ? BaseFacts.Unresolved(baseKey.Record)
            : new BaseFacts(baseKey.Record, true, BaseRecordKind.Static, "Static", $"Base{baseKey.Record.Id:X}", null, null, null, ImmutableArray<string>.Empty, null, null);
    }
}
