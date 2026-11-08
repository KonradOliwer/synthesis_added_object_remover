using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>Results of the step that restores what the cleaned mod changed where it was removed, built by hand for the log and report tests.</summary>
internal static class RestoreWhereRemovedReportScene
{
    public const string Heading = "Restored where the cleaned mod was removed";
    private const int HeightRowLength = TerrainGrid.VerticesPerSide;

    public static readonly ReportContext Normal = new(Bases: null!, Shapes: null!, Detailed: false);
    public static readonly ReportContext Detailed = new(Bases: null!, Shapes: null!, Detailed: true);
    public static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(1.5);

    public static readonly PluginName BaseGame = new("Base.esm");
    public static readonly PluginName Later = new("Later.esp");
    public static readonly PluginName Patch = new("Patch.esp");
    public static readonly RecordKey WaterObject = new(Later, 0x777);

    public static readonly AreaShare Cleared = new(Objects: 214, Standing: 0, Size: 8_000_000, StandingSize: 0, AreaVerdict.Cleared, CountPercent: 0, SizePercent: 0);
    public static readonly AreaShare StillStanding = new(Objects: 10, Standing: 4, Size: 1000, StandingSize: 250, AreaVerdict.StillStanding, CountPercent: 40, SizePercent: 25);
    public static readonly AreaShare NoObjects = new(Objects: 0, Standing: 0, Size: 0, StandingSize: 0, AreaVerdict.NoObjectsAround, CountPercent: 0, SizePercent: 0);

    public static readonly RestoreWhereRemovedResult SwitchedOff = RestoreWhereRemovedResult.SwitchedOff();

    public static string Space => $"worldspace {RecordLabels.Of(TestTargets.Space, "Tamriel")}";

    public static ImmutableArray<CsvTable> BuildTables(RestoreWhereRemovedResult result, ReportContext context, params CloneWritten[] clones) =>
        Tables.BuildRestoredWhereRemovedTables(World(), result, [.. clones], context);

    public static LogSection Section(RestoreWhereRemovedResult result, ReportContext context) =>
        LogSections.BuildRestoreWhereRemovedSection(World(), result, Elapsed, context);

    public static CsvTable ObjectsTable() =>
        Assert.Single(BuildTables(Scene(), Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedObjectsFileName);

    public static CsvTable CellsTable() =>
        Assert.Single(BuildTables(Scene(), Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedCellsFileName);

    public static ImmutableArray<string> Row(CsvTable table, string column, string value) =>
        table.Rows.Single(row => Field(table, row, column) == value);

    /// <summary>The value of a field, without the quotes a comma in it needed.</summary>
    public static string Field(CsvTable table, ImmutableArray<string> row, string column)
    {
        var field = row[table.Header.IndexOf(column)];
        return field.StartsWith('"') ? field[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal) : field;
    }

    /// <summary>Only the given placed changes judged, none of the other kinds.</summary>
    public static RestoreWhereRemovedResult ObjectsOnly(params PlacedChangeJudged[] objects) =>
        Scene() with { Objects = [.. objects], Cells = [], Lands = [], Navmeshes = [], NavmeshGroups = [] };

    public static CollectedObjects World(int targets = 1) =>
        TestScenes.CreateWorld(TestTargets.CreateMany(targets), []) with
        {
            Spaces = new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace, "Skyrim") },
        };

    public static RecordKey Key(uint id) => new(BaseGame, id);

    /// <summary>
    /// Four objects that went back (moved back with a rotation left to a later plugin, shown again, hidden again, own model again),
    /// two refused by a guard (one with a check that stopped), one that stays for its area, one with no objects around, one that
    /// never goes back; four cells; one land; and navmeshes in four groups.
    /// </summary>
    public static RestoreWhereRemovedResult Scene() =>
        new(
            StepOutcome.Performed,
            [
                Restored(Rock(0x901, PlacedParts.Position | PlacedParts.Rotation, later: new Dictionary<PlacedParts, PluginName> { [PlacedParts.Rotation] = Later }), Cleared),
                Restored(Rock(0x902, PlacedParts.Presence, removedBefore: false, later: new Dictionary<PlacedParts, PluginName> { [PlacedParts.Scale] = Patch }), Cleared),
                Restored(Rock(0x903, PlacedParts.Presence, removedBefore: true), Cleared),
                Restored(Rock(0x904, PlacedParts.BaseAndData | PlacedParts.Position), Cleared),
                Refused(Rock(0x905, PlacedParts.Position), Cleared, new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, Key(0x500), Failure: null)),
                Refused(Rock(0x906, PlacedParts.Position), Cleared, new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, By: null, "InvalidOperationException: broke")),
                KeptForArea(Rock(0x907, PlacedParts.Position), StillStanding, AreaVerdict.StillStanding),
                KeptForArea(Rock(0x908, PlacedParts.Position), NoObjects, AreaVerdict.NoObjectsAround),
                NeverRestored(Rock(0x909, PlacedParts.Position) with { WinningMod = Later }, NeverRestoredReason.WinnerLoadsAfterPatch),
            ],
            [
                Cell(1, 2, [CellPart.Lighting, CellPart.Music], Cleared, restored: true),
                Cell(3, 4, [CellPart.Music], Cleared, restored: true, waterKeptBy: WaterObject),
                Cell(5, 6, [CellPart.Music], StillStanding, keptForArea: AreaVerdict.StillStanding, later: Later),
                Cell(7, 8, [CellPart.Lighting], area: null, never: NeverRestoredReason.LinkedFromCleanedMod),
            ],
            [Land()],
            [
                Navmesh(0xA01, NavmeshCase.Edited, NavmeshUndo.Geometry, Cleared),
                Navmesh(0xA02, NavmeshCase.Deleted,NavmeshUndo.BroughtBack, Cleared),
                Navmesh(0xA03, NavmeshCase.Added, NavmeshUndo.Sunk, Cleared),
                Navmesh(0xA04, NavmeshCase.Edited, NavmeshUndo.Geometry, Cleared),
                Navmesh(0xA05, NavmeshCase.Edited, undo: null, StillStanding, keptForArea: AreaVerdict.StillStanding),
            ],
            [
                new NavmeshGroupJudged([Key(0xA01), Key(0xA02)], new NavmeshGroupWrite([], [], []), Refused: null),
                new NavmeshGroupJudged([Key(0xA03)], new NavmeshGroupWrite([], [], []), Refused: null),
                new NavmeshGroupJudged([Key(0xA04)], Write: null, new NavmeshRefusal(NavmeshGuard.EdgeLinkUnmatched, Key(0xA09), Fault: null)),
            ],
            Passes: 3,
            DoorsLeftRemoved: [],
            Failures: [],
            new RestoreWhereRemovedTimes(
                PlansMade: TimeSpan.FromSeconds(1.2),
                WorldAfterRemoval: TimeSpan.FromSeconds(2.3),
                GuardsSetUp: TimeSpan.FromSeconds(3.4),
                Passes: TimeSpan.FromSeconds(4.5),
                InPasses: new RestorePassTimes(
                    PlansRead: TimeSpan.FromSeconds(0.7),
                    FollowUp: TimeSpan.FromSeconds(0.1),
                    WhatStands: TimeSpan.FromSeconds(0.2),
                    PlacedGuards: TimeSpan.FromSeconds(5.6),
                    ChangedTerrain: TimeSpan.FromSeconds(0.3),
                    NavmeshGuards: TimeSpan.FromSeconds(0.4),
                    GroundProtected: TimeSpan.FromSeconds(0.5),
                    WaterKept: TimeSpan.FromSeconds(0.6))));

    /// <summary>
    /// Base game rocks the cleaned mod moved, whose old places are cleared: one goes back with a copy; three go back without one
    /// because they may not be copied, one with a check that stopped.
    /// </summary>
    public static RestoreWhereRemovedResult CopyScene() =>
        ObjectsOnly(
            Restored(Rock(0x930, PlacedParts.Position), Cleared) with { Cloning = new CloneJudged(RefusedBy: null) },
            Restored(Rock(0x931, PlacedParts.Position), Cleared) with { Cloning = new CloneJudged(CloneRefusal.Persistent) },
            Restored(Rock(0x932, PlacedParts.Position), Cleared) with { Cloning = new CloneJudged(CloneRefusal.NotPlainScenery) },
            Restored(Rock(0x933, PlacedParts.Position), Cleared) with
            {
                Cloning = new CloneJudged(CloneRefusal.OnChangedTerrainAsCleanedModsObject, "InvalidOperationException: broke"),
            });

    public static PlacedChangeJudged Restored(RecordChangedByCleanedMod change, AreaShare area) =>
        new(change, Never: null, area, WhereItIsNow: null, Refused: null, IsRestored: true, ChangeJudge.KindOf(change), KeptForArea: null, PlacedBack: null);

    public static PlacedChangeJudged Refused(RecordChangedByCleanedMod change, AreaShare area, PlacedRefusal refusal) =>
        new(change, Never: null, area, WhereItIsNow: null, refusal, IsRestored: false, ChangeJudge.KindOf(change), KeptForArea: null, PlacedBack: null);

    public static PlacedChangeJudged KeptForArea(RecordChangedByCleanedMod change, AreaShare area, AreaVerdict keeping, AreaShare? now = null) =>
        new(change, Never: null, area, now, Refused: null, IsRestored: false, ChangeJudge.KindOf(change), keeping, PlacedBack: null);

    public static PlacedChangeJudged NeverRestored(RecordChangedByCleanedMod change, NeverRestoredReason never) =>
        new(change, never, JudgedPlace: null, WhereItIsNow: null, Refused: null, IsRestored: false, ChangeJudge.KindOf(change), KeptForArea: null, PlacedBack: null);

    public static RecordChangedByCleanedMod Rock(uint id, PlacedParts undo, bool removedBefore = false, IReadOnlyDictionary<PlacedParts, PluginName>? later = null)
    {
        var earlier = TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(new Vector3(10, 20, 30), Vector3.Zero, 1f)) with
        {
            Undo = undo,
            LeftToLaterPlugins = later is null ? ImmutableSortedDictionary<PlacedParts, PluginName>.Empty : later.ToImmutableSortedDictionary(),
        };
        return new RecordChangedByCleanedMod(
            Key(id),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            WinningModPastPatches: TestTargets.TargetName,
            WhereItIsNow: null,
            BaseNow: null,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            earlier,
            Target: null,
            LinkedFromCleanedMod: false,
            new PlacedPresence(removedBefore, Parent: null, OppositeOfParent: false));
    }

    public static CellChangeJudged Cell(
        int x,
        int y,
        CellPart[] undone,
        AreaShare? area,
        bool restored = false,
        RecordKey? waterKeptBy = null,
        AreaVerdict? keptForArea = null,
        PluginName? later = null,
        NeverRestoredReason? never = null)
    {
        var water = new CellWaterCopy(BaseGame, HasWater: false, OwnLevel: null);
        ImmutableArray<PartChange> parts =
        [
            .. undone.Select(part => new PartChange(new CellPartName(part, null), new PartOutcome.Undo())),
            .. later is { } plugin ? new[] { new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.LeftToLaterPlugin(plugin)) } : Array.Empty<PartChange>(),
        ];
        var change = new CellChangedByCleanedMod(
            new RecordKey(BaseGame, (uint)(0x500 + x)), TestTargets.Space, (x, y), parts, LinkedFromCleanedMod: false, TestTargets.TargetName, water, water, DefaultWaterLevel: null);
        return new CellChangeJudged(change, [.. parts.Where(part => part.Outcome is PartOutcome.Undo)], never, area, waterKeptBy, restored, keptForArea);
    }

    /// <summary>
    /// Cell (0, 0): the cleaned mod changed three heights and a colour of the south-west quarter and a height of the north-east
    /// quarter, and the textures of both. Two heights go back, one stays under an object that stays, the north-east quarter's area
    /// still stands.
    /// </summary>
    public static LandChangeJudged Land()
    {
        var northEast = 20 * HeightRowLength + 20;
        var cell = new ExteriorCell(TestTargets.Space, 0, 0);
        var change = new LandChangedByCleanedMod(
            cell,
            new RecordKey(BaseGame, 0x650),
            PointSet.Of([0, 1, HeightRowLength, northEast]),
            PointSet.Of([0]),
            QuarterSet.Of([0, 3]),
            UndoRest: false,
            new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), default, Rest: false, []),
            TestTargets.TargetName,
            Heights: null);
        return LandOutcomes.Judged(
            change,
            [new(Cleared), new(Cleared), new(Cleared), new(StillStanding)],
            PointSet.Of([0, HeightRowLength]),
            PointSet.Of([0]),
            QuarterSet.Of([0]),
            texturesKept: default,
            rest: false,
            [new LandPointsKept(TerrainKeptReason.UnderObjectThatStays, PointSet.Of([1]))],
            finalHeights: null,
            CellEdgeHeights.Unknown);
    }

    /// <summary>
    /// Cell (2, 0): the cleaned mod raised the point (5, 5) of the south-west quarter, which goes back, and the point (16, 5) on its line
    /// to the south-east quarter, whose area still stands, which is kept for that.
    /// </summary>
    public static LandChangeJudged LandNextToAQuarterThatStays()
    {
        var inside = 5 * HeightRowLength + 5;
        var onTheLine = 5 * HeightRowLength + 16;
        var change = new LandChangedByCleanedMod(
            new ExteriorCell(TestTargets.Space, 2, 0),
            new RecordKey(BaseGame, 0x652),
            PointSet.Of([inside, onTheLine]),
            PointSet.Of([]),
            QuarterSet.Of([]),
            UndoRest: false,
            new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), default, Rest: false, []),
            TestTargets.TargetName,
            Heights: null);
        return LandOutcomes.Judged(
            change,
            [new(Cleared), new(StillStanding), new(Cleared), new(Cleared)],
            PointSet.Of([inside]),
            PointSet.Of([]),
            QuarterSet.Of([]),
            texturesKept: default,
            rest: false,
            [new LandPointsKept(TerrainKeptReason.NextToQuarterThatStays, PointSet.Of([onTheLine]))],
            finalHeights: null,
            CellEdgeHeights.Unknown);
    }

    /// <summary>Cell (1, 0): the cleaned mod changed only the rest of the record, which goes back unless the last quarter's area still stands.</summary>
    public static LandChangeJudged RestOnlyLand(bool lastQuarterStillStands)
    {
        var change = new LandChangedByCleanedMod(
            new ExteriorCell(TestTargets.Space, 1, 0),
            new RecordKey(BaseGame, 0x651),
            PointSet.Of([]),
            PointSet.Of([]),
            QuarterSet.Of([]),
            UndoRest: true,
            new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), default, Rest: false, []),
            TestTargets.TargetName,
            Heights: null);
        return LandOutcomes.Judged(
            change,
            [new(Cleared), new(Cleared), new(Cleared), new(lastQuarterStillStands ? StillStanding : Cleared)],
            PointSet.Of([]),
            PointSet.Of([]),
            QuarterSet.Of([]),
            texturesKept: default,
            rest: !lastQuarterStillStands,
            [],
            finalHeights: null,
            CellEdgeHeights.Unknown);
    }

    public static NavmeshChangeJudged Navmesh(uint id, NavmeshCase navmeshCase, NavmeshUndo? undo, AreaShare area, AreaVerdict? keptForArea = null)
    {
        var cell = Key(0x600);
        var change = new NavmeshChangedByCleanedMod(
            Key(id),
            TestTargets.Space,
            cell,
            TestTargets.TargetName,
            navmeshCase,
            Geometry: navmeshCase == NavmeshCase.Edited ? new PartOutcome.Undo() : new PartOutcome.Unchanged(),
            Presence: navmeshCase == NavmeshCase.Edited ? new PartOutcome.Unchanged() : new PartOutcome.Undo(),
            MapEntry: new PartOutcome.Unchanged(),
            Earlier: null,
            new NavmeshShape(TestTargets.TargetName, cell, IsDeleted: false, [], [], [], [], MapEntry: null),
            ReplacedBy: [],
            Neighbours: [],
            Doors: [],
            NamedInPreferredPaths: false,
            WinningMapMod: null,
            CellDeletedInAVersion: false,
            LaterWinner: null,
            WinningMapEntry: null);
        return new NavmeshChangeJudged(change, Never: null, area, undo, keptForArea, NothingGoesBack: false);
    }
}
