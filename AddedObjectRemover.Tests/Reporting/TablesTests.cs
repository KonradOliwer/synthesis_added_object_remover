using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using P3Float = Noggog.P3Float;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The report tables, written by <see cref="ReportFolder"/>, have the file names, headers and formatting of the checked-in golden files.</summary>
public sealed class TablesTests : IDisposable
{
    private const float TouchDistance = 1f;
    private const float Threshold = 0.5f;

    private static readonly KeepReason QuestReason = new(KeepKind.NonPlacedReference, "QUST record", "linked from QUST");

    private static readonly TestStatic TableModel = new(
        new FormKey(TestTargets.TargetMod, 0x711), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic ItemModel = new(
        new FormKey(TestTargets.TargetMod, 0x712), @"test\item,1.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));

    private static readonly TestStatic PlankModel = new(
        new FormKey(TestTargets.TargetMod, 0x713), @"test\plank.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-30.5f, -2, 0), new Vector3(30.5f, 2, 4))));

    private static readonly ShapeCatalog Shapes =
        TestShapes.Create(TestTargets.TargetMod, "TablesData", TableModel, ItemModel, PlankModel);

    private static readonly IBaseFacts Bases = new FakeBases(unresolved: ItemModel.FormKey);

    private static readonly ReportContext Context = new(Bases, Shapes, Detailed: false);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "aor-tables-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TouchTables_MatchTheirGoldenFile()
    {
        var scene = TouchScene();
        var options = new FollowUpOptions(FollowUpRemovalMode.EverythingTouching, TouchDistance, Threshold);
        var tables = Tables.Build(
            scene.Outcome, new Explanations(scene.Run.Explanation, null), Reports("new"), null, options, Context);

        Assert.NotEmpty(scene.Run.Explanation!.Edges);
        Assert.Single(scene.Run.Components.Members);
        Assert.NotEmpty(scene.Run.FollowUpRounds.SelectMany(round => Decisions.KeptIn(scene.Run.Ledger, round)));
        AssertMatchesGolden("tables-edges", Tables.EdgesFileName, tables);
        AssertMatchesGolden("tables-components", Tables.ComponentsFileName, tables);
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

        AssertMatchesGolden("tables-anchoring", Tables.AnchoringFileName, [table]);
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

        AssertMatchesGolden("tables-mesh-origins", Tables.MeshOriginsFileName, [Tables.MeshOrigins(origins)]);
    }

    [Fact]
    public void LeftoverTable_MatchesTheOldWriterByteForByte()
    {
        var world = CreateWorld(CreateTargets(), out var rival);
        var surroundings = new SectorAreas(50);
        surroundings.Add(DirectionSector.East, 100f, removed: true);
        surroundings.Add(DirectionSector.West, 40.5f, removed: false);
        LeftoverEvaluation[] evaluations =
        [
            new(3, InvisibleObjectKind.DoorMarkers, 250f, rival, surroundings, LeftoverDecision.RemovedInsideOtherObject, KeepReason: null),
            new(1, InvisibleObjectKind.IdleMarkers, 100.5f, null, new SectorAreas(50), LeftoverDecision.KeptReferenced, QuestReason),
        ];
        var relocations = new RelocationResult(
            [new Relocation(evaluations[0], new Vector3(1, 2, 3), new Vector3(4, 5, 9), RelocationSurface.Terrain, LeftHomeCell: false)], []);

        var table = Tables.Leftovers(world, evaluations, relocations, Context);

        AssertMatchesGolden("tables-leftovers", Tables.LeftoversFileName, [table]);
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

        AssertMatchesGolden("tables-hints", Tables.HintsFileName, [Tables.Hints(world, hints)]);
    }

    [Fact]
    public void Build_WritesNothingWhenReportFilesAreOff()
    {
        var scene = TouchScene();
        var options = new FollowUpOptions(FollowUpRemovalMode.EverythingTouching, TouchDistance, Threshold);

        var tables = Tables.Build(
            scene.Outcome, new Explanations(scene.Run.Explanation, null), new ReportOptions(false, Folder("off")), LeftoverOptionsOn(), options, Context);

        Assert.Empty(tables);
    }

    [Fact]
    public void Build_TouchExplanationsGiveEdgesComponentsAndHints()
    {
        var scene = TouchScene();
        var options = new FollowUpOptions(FollowUpRemovalMode.EverythingTouching, TouchDistance, Threshold);

        var tables = Tables.Build(scene.Outcome, new Explanations(scene.Run.Explanation, null), Reports("new"), null, options, Context);

        Assert.Equal(
            [Tables.EdgesFileName, Tables.ComponentsFileName, Tables.HintsFileName], tables.Select(table => table.FileName));
    }

    [Fact]
    public void Build_WithoutExplanationsLeftoversOrSupportRoundsGivesOnlyHints()
    {
        var scene = TouchScene();
        var options = new FollowUpOptions(FollowUpRemovalMode.EverythingTouching, TouchDistance, Threshold);

        var tables = Tables.Build(scene.Outcome, Explanations.None, Reports("new"), null, options, Context);

        Assert.Equal([Tables.HintsFileName], tables.Select(table => table.FileName));
    }

    [Theory]
    [InlineData(FollowUpRemovalMode.ObjectsSupportedByIt, true, true)]
    [InlineData(FollowUpRemovalMode.ObjectsSupportedByIt, false, false)]
    [InlineData(FollowUpRemovalMode.EverythingTouching, true, false)]
    [InlineData(FollowUpRemovalMode.Nothing, true, false)]
    public void Build_WritesAnchoringOnlyForSupportRoundsWithSeeds(FollowUpRemovalMode mode, bool hadSeeds, bool expected)
    {
        var scene = TouchScene();
        var followUp = scene.Outcome.FollowUp with { Mode = mode, HadSeeds = hadSeeds, Rounds = [], Evidence = [] };
        var outcome = scene.Outcome with { FollowUp = followUp };

        var tables = Tables.Build(outcome, Explanations.None, Reports("new"), null, new FollowUpOptions(mode, TouchDistance, Threshold), Context);

        Assert.Equal(expected, tables.Any(table => table.FileName == Tables.AnchoringFileName));
    }

    [Fact]
    public void Build_ListsTheFilesInTheOrderTheStepsRan()
    {
        var scene = TouchScene();
        var options = new FollowUpOptions(FollowUpRemovalMode.ObjectsSupportedByIt, TouchDistance, Threshold);
        var outcome = scene.Outcome with { FollowUp = scene.Outcome.FollowUp with { Mode = options.Mode, Rounds = [], Evidence = [] } };
        var origins = new Explanations(null, [new MeshOrigin("m.nif", [], 1, default, Vector3.Zero, "other")]);

        var tables = Tables.Build(outcome, origins, Reports("new"), LeftoverOptionsOn(), options, Context);

        Assert.Equal(
            [Tables.AnchoringFileName, Tables.MeshOriginsFileName, Tables.LeftoversFileName, Tables.HintsFileName],
            tables.Select(table => table.FileName));
    }

    private sealed record Scene(Outcome Outcome, TestTouchCascade.Run Run);

    /// <summary>Tables 0 and 2 are seeds; the plank 1 and item 4 are removed by touch; the referenced item 3 is held; target 4 has a comma in its Editor ID.</summary>
    private static Scene TouchScene()
    {
        var targets = CreateTargets();
        var protection = Protection.Build(
            [.. targets], [], TestTargets.References(targets.Count, new Dictionary<int, KeepReason> { [3] = QuestReason }));
        var run = TestTouchCascade.Execute(targets, Shapes, protection, [0, 2], TouchDistance, threads: 2, collectDiagnostics: true);
        var world = CreateWorld(targets, out _);
        var looks = TestSeededLedger.AllVisible(targets.Count);
        var decided = new Decided(
            world, looks, null!, null!, protection, null!, null!, run.Result, null, LeftoverResult.None, RelocationResult.None, run.Ledger);
        var outcome = new Outcome(decided, [.. Decisions.Removals(run.Ledger, world, LeftoverResult.None)], null!, [], [], default);
        return new Scene(outcome, run);
    }

    private static List<TargetObject> CreateTargets() =>
    [
        Place(0, TableModel, new Vector3(0, 0, 0)),
        Place(1, PlankModel, new Vector3(50, 0, 3)),
        Place(2, TableModel, new Vector3(100, 0, 0)),
        Place(3, ItemModel, new Vector3(5, 5, 10)),
        Place(4, ItemModel, new Vector3(105, 5, 10)) with { EditorId = "Item,\"4\"", CellName = "Cell, one" },
    ];

    private static TargetObject Place(int index, TestStatic model, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position, zRadians: 0.3f * index, scale: 1f + index * 0.25f), model.Ref, TestTargets.Space);

    private static World CreateWorld(List<TargetObject> targets, out OtherObject rival)
    {
        rival = new OtherObject(
            new OtherId(0),
            new FormKey(ModKey.FromNameAndExtension("Other.esp"), 0x10),
            TestTargets.Space,
            ModKey.FromNameAndExtension("Other.esp"),
            EditorId: "Rival,1",
            Base: null,
            Vector3.Zero,
            default(P3Float),
            Scale: 1f,
            IsPrimitive: false,
            HasMapMarker: false);
        return new World(
            [.. targets],
            [rival],
            Collected<ImmutableArray<OtherObject>>.NotCollected,
            [],
            [.. TestTargets.References(targets.Count)],
            new Dictionary<FormKey, string> { [TestTargets.Space] = "Tamriel, Test" },
            new ReadCounts(0, 0, 0, 0, 0, 0, 0),
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

    private static LeftoverOptions LeftoverOptionsOn() => new(100f, 50, 50, 50, new HashSet<InvisibleObjectKind>(), default);

    private ReportOptions Reports(string name) => new(true, Folder(name));

    private string Folder(string name) => Path.Combine(_root, name);

    private void AssertMatchesGolden(string goldenName, string fileName, ImmutableArray<CsvTable> tables)
    {
        var table = tables.Single(candidate => candidate.FileName == fileName);
        var result = ReportFolder.Write(Reports("new"), [table]);

        Assert.Empty(result.Warnings);
        GoldenFiles.AssertMatches(goldenName, File.ReadAllLines(result.Written.Single().Path));
    }

    private sealed class FakeBases(FormKey unresolved) : IBaseFacts
    {
        public BaseFacts Of(BaseRef baseRef) => baseRef.FormKey == unresolved
            ? BaseFacts.Unresolved(baseRef.FormKey)
            : new BaseFacts(baseRef.FormKey, true, BaseRecordKind.Static, "Static", $"Base{baseRef.FormKey.ID:X}", null, null, null, false, null, null);
    }
}
