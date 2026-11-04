using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    /// <summary>The share of an area: objects, standing, size, standing size, standing percent and verdict.</summary>
    private const int AreaColumnCount = 7;

    private static readonly ImmutableArray<string> AreaHeader = ["objects", "standing", "size", "standingSize", "standingPercentByCount", "standingPercentBySize", "verdict"];

    private static readonly ImmutableArray<string> RestoredWhereRemovedObjectsHeader =
    [
        "formKey", "editorId", "base", "space", "cellFormKey", "cellEditorId", "winningMod", "oldX", "oldY", "oldZ", "undone", "leftToLaterPlugins",
        .. AreaHeader,
        .. AreaHeader.Select(column => "now" + char.ToUpperInvariant(column[0]) + column[1..]),
        "result", "copyFormKey", "copyRefusedBy",
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
    /// <param name="clones">The copies the patch added, which the objects file names by their original.</param>
    public static ImmutableArray<CsvTable> BuildRestoredWhereRemovedTables(CollectedObjects world, RestoreWhereRemovedResult result, ImmutableArray<CloneWritten> clones, ReportContext context)
    {
        var judged = new RestoreJudgements(result);
        var label = RestoreOutcomes.LabelsIn(world);
        var copyOf = clones.ToDictionary(clone => clone.Original, clone => clone.Copy);
        ImmutableArray<ImmutableArray<string>> objects = [.. result.Objects.Select(change => RestoredWhereRemovedObjectRow(world, change, copyOf, label, context))];
        ImmutableArray<ImmutableArray<string>> cells =
        [
            .. result.Cells.Select(cell => RestoredWhereRemovedCellRow(world, cell, label)),
            .. RestoredWhereRemovedLandRows(world, result.Lands),
            .. judged.Navmeshes.Select(navmesh => RestoredWhereRemovedNavmeshRow(world, navmesh, label)),
        ];
        return
        [
            .. new[]
            {
                CsvTable.WhenAnyRow(ReportFileNames.RestoredWhereRemovedObjectsFileName, RestoredWhereRemovedObjectsHeader, objects),
                CsvTable.WhenAnyRow(ReportFileNames.RestoredWhereRemovedCellsFileName, RestoredWhereRemovedCellsHeader, cells),
            }.OfType<CsvTable>(),
        ];
    }

    /// <param name="copyOf">The copy the patch added of each original that has one.</param>
    /// <param name="label">Names a record a guard found.</param>
    private static ImmutableArray<string> RestoredWhereRemovedObjectRow(
        CollectedObjects world, PlacedChangeJudged judged, IReadOnlyDictionary<RecordKey, RecordKey> copyOf, Func<RecordKey, string> label, ReportContext context)
    {
        var change = judged.Change;
        var copy = copyOf.TryGetValue(change.Key, out var written) ? written.ToString() : null;
        var refused = judged.Cloning is { RefusedBy: not null } cloning ? RestoreWhereRemovedText.CopyRefused(cloning) : null;
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
            .. AreaColumns(judged.OldPlace),
            .. AreaColumns(judged.WhereItIsNow),
            Quote(RestoreWhereRemovedText.Result(RestoreOutcomes.KeptBecause(judged, label), RestoreOutcomes.GoesBackWithCopy(judged))),
            OptionalText(copy),
            OptionalText(refused),
        ]);
    }

    /// <param name="label">Names the record that keeps the water level.</param>
    private static ImmutableArray<string> RestoredWhereRemovedCellRow(CollectedObjects world, CellChangeJudged judged, Func<RecordKey, string> label)
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
            RestoreWhereRemovedText.Result(RestoreOutcomes.KeptBecause(judged, label), RestoreWhereRemovedText.WaterLevelPart, RestoreOutcomes.WhyWaterIsKept(judged, label)),
            keptPoints: null);
    }

    private static IEnumerable<ImmutableArray<string>> RestoredWhereRemovedLandRows(CollectedObjects world, IEnumerable<LandChangeJudged> lands)
    {
        foreach (var land in lands)
        {
            foreach (var quarter in land.ChangedQuarters) yield return RestoredWhereRemovedQuarterRow(world, land, quarter);
            if (RestoreOutcomes.HoldsOnlyTheRest(land)) yield return RestoredWhereRemovedRestRow(world, land);
        }
    }

    private static ImmutableArray<string> RestoredWhereRemovedRestRow(CollectedObjects world, LandChangeJudged land) =>
        RestoredWhereRemovedLandRow(
            "rest",
            world,
            land,
            quarter: null,
            RestoreWhereRemovedText.RestOfLandRecord,
            leftToLaterPlugins: null,
            area: null,
            RestoreWhereRemovedText.Result(RestoreOutcomes.KeptBecauseOfRest(land)),
            keptPoints: null);

    private static ImmutableArray<string> RestoredWhereRemovedQuarterRow(CollectedObjects world, LandChangeJudged land, LandQuarterOutcome quarter) =>
        RestoredWhereRemovedLandRow(
            "quarter",
            world,
            land,
            RestoreWhereRemovedText.Quarter(quarter.Quarter),
            RestoreWhereRemovedText.Pieces(quarter.Changed),
            LaterTerrain(land.Change.LeftToLater, quarter),
            quarter.Area,
            RestoreWhereRemovedText.Result(
                RestoreOutcomes.KeptBecause(land, quarter),
                RestoreWhereRemovedText.GroundTexturesPart,
                quarter.TexturesKeptForObjectThatStays ? RestoreWhereRemovedText.TexturesKeptForObjectThatStays : null),
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
        string result,
        ImmutableArray<int>? keptPoints)
    {
        var cell = land.Change.Cell;
        return RestoredWhereRemovedRow(
            kind, Describe.Space(world, cell.WorldspaceKey), (cell.X, cell.Y), cell: null, quarter, navmesh: null, undone, leftToLaterPlugins, area, result, keptPoints);
    }

    /// <param name="label">Names a record a guard found.</param>
    private static ImmutableArray<string> RestoredWhereRemovedNavmeshRow(CollectedObjects world, NavmeshOutcome outcome, Func<RecordKey, string> label)
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
            .. AreaColumns(area),
            Quote(result),
            .. keptPoints is { } points ? points.Select(Int) : Enumerable.Repeat(string.Empty, TerrainKeptReasons.Length),
        ]);

    /// <summary>The columns of <see cref="AreaHeader"/>; empty for an area that was not judged.</summary>
    private static IEnumerable<string> AreaColumns(AreaShare? area) =>
        CsvRow.EmptyOrValues(
            area,
            AreaColumnCount,
            share =>
            [
                Int(share.Objects),
                Int(share.Standing),
                Number(share.Size),
                Number(share.StandingSize),
                Number((float)share.CountPercent),
                Number((float)share.SizePercent),
                Quote(share.Verdict.ToString()),
            ]);

    /// <summary>E.g. "12 heights (Later.esp, Other.esp)": what a later plugin changed again in the quarter, with the plugins that did so in the land.</summary>
    private static string? LaterTerrain(LandLeftToLater later, LandQuarterOutcome quarter)
    {
        var pieces = RestoreWhereRemovedText.Pieces(quarter.Later);
        return pieces.Length == 0 ? null : $"{pieces} ({string.Join(Describe.ListSeparator, later.Plugins)})";
    }
}
