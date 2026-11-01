using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    /// <summary>The share of an area: objects, standing, size, standing size, standing percent and verdict.</summary>
    private const int AreaColumnCount = 6;

    private static readonly ImmutableArray<string> AreaHeader = ["objects", "standing", "size", "standingSize", "standingPercent", "verdict"];

    private static readonly ImmutableArray<string> RestoredWhereRemovedObjectsHeader =
    [
        "formKey", "editorId", "base", "space", "cellFormKey", "cellEditorId", "winningMod", "oldX", "oldY", "oldZ", "undone", "leftToLaterPlugins",
        .. AreaHeader,
        .. AreaHeader.Select(column => "now" + char.ToUpperInvariant(column[0]) + column[1..]),
        "result",
    ];

    private static readonly ImmutableArray<TerrainKeptReason> TerrainKeptReasons = [.. Enum.GetValues<TerrainKeptReason>()];

    private static readonly ImmutableArray<string> RestoredWhereRemovedCellsHeader =
    [
        "kind", "space", "gridX", "gridY", "cell", "quarter", "navmesh", "undone", "leftToLaterPlugins",
        .. AreaHeader,
        "result",
        .. TerrainKeptReasons.Select(reason => "kept" + reason),
    ];

    /// <summary>The two files of what the cleaned mod changed that goes back where it was removed, each only when it has a row.</summary>
    /// <remarks>
    /// The objects file has a row per placed record judged, in record key order. The cells file lists the cells in record key order,
    /// then each land's quarters that hold a change in the order of the lands' cells and of the quarters, followed by one row for the
    /// rest of the record when that is all of the land that could go back, then the navmeshes in record key order; a row for a terrain
    /// quarter or for the rest of a land's record gives the cell's grid, one for a cell or a navmesh its record.
    /// </remarks>
    public static ImmutableArray<CsvTable> BuildRestoredWhereRemovedTables(CollectedObjects world, RestoreWhereRemovedResult result, ClearedAreaMeasure measure, ReportContext context)
    {
        var judged = new RestoreJudgements(result);
        var label = RestoreOutcomes.LabelsIn(world);
        ImmutableArray<ImmutableArray<string>> objects = [.. result.Objects.Select(change => RestoredWhereRemovedObjectRow(world, change, measure, label, context))];
        ImmutableArray<ImmutableArray<string>> cells =
        [
            .. result.Cells.Select(cell => RestoredWhereRemovedCellRow(world, cell, measure, label)),
            .. RestoredWhereRemovedLandRows(world, judged.Lands, measure),
            .. judged.Navmeshes.Select(navmesh => RestoredWhereRemovedNavmeshRow(world, navmesh, measure, label)),
        ];
        return
        [
            .. new[]
            {
                TableWhenAnyRow(ReportFileNames.RestoredWhereRemovedObjectsFileName, RestoredWhereRemovedObjectsHeader, objects),
                TableWhenAnyRow(ReportFileNames.RestoredWhereRemovedCellsFileName, RestoredWhereRemovedCellsHeader, cells),
            }.OfType<CsvTable>(),
        ];
    }

    private static CsvTable? TableWhenAnyRow(string fileName, ImmutableArray<string> header, ImmutableArray<ImmutableArray<string>> rows) =>
        rows.IsEmpty ? null : new CsvTable(fileName, header, rows);

    /// <param name="label">Names a record a guard found.</param>
    private static ImmutableArray<string> RestoredWhereRemovedObjectRow(
        CollectedObjects world, PlacedChangeJudged judged, ClearedAreaMeasure measure, Func<RecordKey, string> label, ReportContext context)
    {
        var change = judged.Change;
        return CsvRow.Of(
        [
            Quote(change.Key.ToString()),
            OptionalText(change.EditorId),
            Quote(RecordNames.DescribeBase(context.Bases, change.BaseNow)),
            Quote(Describe.Space(world, change.SpaceKey)),
            Quote(change.Cell.Key.ToString()),
            OptionalText(change.Cell.EditorId),
            Quote(change.WinningMod.ToString()),
            .. Numbers(change.Earlier.Placement.Position),
            Quote(PartNames(change.Earlier.Undo)),
            OptionalText(LeftToLaterPlugins(change.Earlier)),
            .. AreaColumns(judged.OldPlace, measure),
            .. AreaColumns(judged.WhereItIsNow, measure),
            Quote(RestoreWhereRemovedText.Result(RestoreOutcomes.KeptBecause(judged, label))),
        ]);
    }

    /// <param name="label">Names the record that keeps the water level.</param>
    private static ImmutableArray<string> RestoredWhereRemovedCellRow(CollectedObjects world, CellChangeJudged judged, ClearedAreaMeasure measure, Func<RecordKey, string> label)
    {
        var change = judged.Change;
        return RestoredWhereRemovedRow(
            "cell",
            Describe.Space(world, change.SpaceKey),
            change.Grid,
            cell: change.Cell.ToString(),
            quarter: null,
            navmesh: null,
            string.Join(Describe.ListSeparator, judged.Undone.Select(part => RestoreWhereRemovedText.PartOfCell(part.Name.Part)).Distinct()),
            Describe.PartsByPlugin(RestoreOutcomes.LeftToLaterPlugins(change)),
            judged.Area,
            measure,
            RestoreWhereRemovedText.Result(RestoreOutcomes.KeptBecause(judged, label)),
            keptPoints: null);
    }

    private static IEnumerable<ImmutableArray<string>> RestoredWhereRemovedLandRows(CollectedObjects world, IEnumerable<LandJudgement> lands, ClearedAreaMeasure measure)
    {
        foreach (var land in lands)
        {
            foreach (var quarter in land.Quarters.Where(facts => facts.HoldsChange)) yield return RestoredWhereRemovedQuarterRow(world, land.Land, quarter, measure);
            if (RestoreOutcomes.HoldsOnlyTheRest(land)) yield return RestoredWhereRemovedRestRow(world, land.Land, measure);
        }
    }

    private static ImmutableArray<string> RestoredWhereRemovedRestRow(CollectedObjects world, LandChangeJudged land, ClearedAreaMeasure measure) =>
        RestoredWhereRemovedLandRow(
            "rest",
            world,
            land,
            quarter: null,
            RestoreWhereRemovedText.RestOfLandRecord,
            leftToLaterPlugins: null,
            area: null,
            measure,
            RestoreWhereRemovedText.Result(RestoreOutcomes.KeptBecauseOfRest(land)),
            keptPoints: null);

    private static ImmutableArray<string> RestoredWhereRemovedQuarterRow(CollectedObjects world, LandChangeJudged land, LandQuarterFacts quarter, ClearedAreaMeasure measure) =>
        RestoredWhereRemovedLandRow(
            "quarter",
            world,
            land,
            RestoreWhereRemovedText.Quarter(quarter.Quarter),
            TerrainPieces(quarter.Heights, quarter.Colours, quarter.Textures),
            LaterTerrain(land.Change.LeftToLater, quarter),
            RestoreOutcomes.AreaOf(quarter),
            measure,
            RestoreWhereRemovedText.Result(RestoreOutcomes.KeptBecause(land, quarter)),
            keptPoints: TerrainKeptReasons.Select(reason => quarter.Kept.Where(entry => entry.Reason == reason).Sum(entry => entry.Points)).ToImmutableArray());

    /// <param name="quarter">The quarter's name; null for the rest of the record.</param>
    private static ImmutableArray<string> RestoredWhereRemovedLandRow(
        string kind,
        CollectedObjects world,
        LandChangeJudged land,
        string? quarter,
        string undone,
        string? leftToLaterPlugins,
        AreaShare? area,
        ClearedAreaMeasure measure,
        string result,
        ImmutableArray<int>? keptPoints)
    {
        var cell = land.Change.Cell;
        return RestoredWhereRemovedRow(
            kind, Describe.Space(world, cell.WorldspaceKey), (cell.X, cell.Y), cell: null, quarter, navmesh: null, undone, leftToLaterPlugins, area, measure, result, keptPoints);
    }

    /// <param name="label">Names a record a guard found.</param>
    private static ImmutableArray<string> RestoredWhereRemovedNavmeshRow(CollectedObjects world, NavmeshOutcome outcome, ClearedAreaMeasure measure, Func<RecordKey, string> label)
    {
        var change = outcome.Judged.Change;
        return RestoredWhereRemovedRow(
            "navmesh",
            Describe.Space(world, change.SpaceKey),
            grid: null,
            cell: change.Cell.ToString(),
            quarter: null,
            navmesh: change.Navmesh.ToString(),
            string.Join(Describe.ListSeparator, RestoreOutcomes.UndoneParts(change)),
            Describe.PartsByPlugin(RestoreOutcomes.LeftToLaterPlugins(change)),
            outcome.Judged.Area,
            measure,
            RestoreWhereRemovedText.Result(RestoreOutcomes.KeptBecause(outcome, label)),
            keptPoints: null);
    }

    /// <param name="grid">The grid of the cell; null for an interior and for a navmesh.</param>
    /// <param name="keptPoints">For a terrain quarter, the points kept for each reason in <see cref="TerrainKeptReasons"/>; null for the other kinds, which have no points.</param>
    private static ImmutableArray<string> RestoredWhereRemovedRow(
        string kind,
        string space,
        (int X, int Y)? grid,
        string? cell,
        string? quarter,
        string? navmesh,
        string undone,
        string? leftToLaterPlugins,
        AreaShare? area,
        ClearedAreaMeasure measure,
        string result,
        ImmutableArray<int>? keptPoints) =>
        CsvRow.Of(
        [
            Quote(kind),
            Quote(space),
            OptionalText(grid is { } column ? Int(column.X) : null),
            OptionalText(grid is { } row ? Int(row.Y) : null),
            OptionalText(cell),
            OptionalText(quarter),
            OptionalText(navmesh),
            Quote(undone),
            OptionalText(leftToLaterPlugins is { Length: > 0 } ? leftToLaterPlugins : null),
            .. AreaColumns(area, measure),
            Quote(result),
            .. keptPoints is { } points ? points.Select(Int) : Enumerable.Repeat(string.Empty, TerrainKeptReasons.Length),
        ]);

    /// <summary>The columns of <see cref="AreaHeader"/>; empty for an area that was not judged.</summary>
    private static IEnumerable<string> AreaColumns(AreaShare? area, ClearedAreaMeasure measure) =>
        CsvRow.EmptyOrValues(
            area,
            AreaColumnCount,
            share =>
            [
                Int(share.Objects),
                Int(share.Standing),
                Number(share.Size),
                Number(share.StandingSize),
                Number((float)ClearedAreaText.StandingPercent(share, measure)),
                Quote(share.Verdict.ToString()),
            ]);

    /// <summary>E.g. "210 heights, 3 vertex colours, ground textures".</summary>
    private static string TerrainPieces(int heights, int colours, bool textures) =>
        string.Join(
            Describe.ListSeparator,
            new[]
            {
                heights > 0 ? TextFormat.Count(heights, "height", "heights") : null,
                colours > 0 ? TextFormat.Count(colours, "vertex colour", "vertex colours") : null,
                textures ? "ground textures" : null,
            }.OfType<string>());

    /// <summary>E.g. "12 heights (Later.esp, Other.esp)": what a later plugin changed again in the quarter, with the plugins that did so in the land.</summary>
    private static string? LaterTerrain(LandLeftToLater later, LandQuarterFacts quarter)
    {
        var pieces = TerrainPieces(quarter.LaterHeights, quarter.LaterColours, quarter.LaterTextures);
        return pieces.Length == 0 ? null : $"{pieces} ({string.Join(Describe.ListSeparator, later.Plugins)})";
    }
}
