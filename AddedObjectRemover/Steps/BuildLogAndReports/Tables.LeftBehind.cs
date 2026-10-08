using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using static AddedObjectRemover.CsvFormat;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    /// <summary>Target x, y, z, distance moved and the surface moved onto.</summary>
    private const int MoveColumnCount = 5;

    private static readonly ImmutableArray<string> LeftBehindHeader =
    [
        "formKey", "editorId", "base", "baseType", "invisibleType", "space", "cell", "x", "y", "z",
        "radius", "insideObject", "insideObjectPlugin",
        .. CompassDirections.All.SelectMany(sector => new[] { $"{sector}Total", $"{sector}Removed", $"{sector}State" }),
        "occupiedDirections", "removedDirections", "decision", "reason",
        "movedToX", "movedToY", "movedToZ", "moveDistance", "movedOnto",
    ];

    /// <param name="evaluations">Listed ordered by record key.</param>
    public static CsvTable LeftBehind(
        CollectedObjects world, IEnumerable<LeftBehindCheck> evaluations, MarkerMoves markerMoves, ReportContext context)
    {
        var movesByTarget = PerIndexTable<KeptMarkerMove>.From(markerMoves.Moved, move => move.Evaluation.TargetIndex);
        var rows = evaluations
            .OrderBy(evaluation => world.Targets[evaluation.TargetIndex].Key, RecordKeyTextOrder.Comparer)
            .Select(evaluation => LeftBehindRow(evaluation, world, context.Bases, movesByTarget.TryGet(evaluation.TargetIndex, out var move) ? move : null));
        return new CsvTable(ReportFileNames.LeftBehindFileName, LeftBehindHeader, [.. rows]);
    }

    private static ImmutableArray<string> LeftBehindRow(LeftBehindCheck evaluation, CollectedObjects world, IBaseFacts bases, KeptMarkerMove? move)
    {
        var target = world.Targets[evaluation.TargetIndex];
        var surroundings = evaluation.Surroundings;
        var position = target.Transform.Position;
        var inside = evaluation.ContainingObject;
        return CsvRow.Of(
        [
            Quote(target.Key.ToString()),
            OptionalText(target.EditorId),
            Quote(RecordNames.DescribeBase(bases, target.Base)),
            Quote(DescribeBaseType(bases, target.Base)),
            Quote(evaluation.Kind.ToString()),
            Quote(Describe.Space(world, target.SpaceKey)),
            Quote(Describe.CellOrEmpty(target)),
            .. Numbers(position),
            Number(evaluation.Radius),
            OptionalText(inside?.Key.ToString()),
            OptionalText(inside?.WinningMod.ToString()),
            .. CompassDirections.All.SelectMany(sector => new[]
            {
                Number(surroundings.Total(sector)), Number(surroundings.Removed(sector)), Quote(surroundings.State(sector).ToString()),
            }),
            Int(surroundings.OccupiedCount),
            Int(surroundings.RemovedCount),
            Quote(evaluation.IsRemoved ? "removed" : "kept"),
            Quote(LeftBehindOutcomeText.DescribeReason(evaluation)),
            .. CsvRow.EmptyOrValues(move, MoveColumnCount, MoveFields),
        ]);
    }

    private static IEnumerable<string> MoveFields(KeptMarkerMove move) =>
        [.. Numbers(move.To), Number(move.Distance), Quote(move.Surface.ToString())];

    private static string DescribeBaseType(IBaseFacts bases, BaseKey? baseKey) =>
        baseKey is { } key && bases.Of(key) is { Resolved: true } facts ? facts.RecordTypeName! : string.Empty;
}
