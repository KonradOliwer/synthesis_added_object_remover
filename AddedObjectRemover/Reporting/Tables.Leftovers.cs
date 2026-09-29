using System.Collections.Immutable;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover;

internal static partial class Tables
{
    private static readonly ImmutableArray<string> LeftoversHeader =
    [
        "formKey", "editorId", "base", "baseType", "invisibleType", "space", "cell", "x", "y", "z",
        "radius", "insideObject", "insideObjectPlugin",
        .. SectorAreas.All.SelectMany(sector => new[] { $"{sector}Total", $"{sector}Removed", $"{sector}State" }),
        "occupiedDirections", "removedDirections", "decision", "reason",
        "movedToX", "movedToY", "movedToZ", "moveDistance", "movedOnto",
    ];

    /// <param name="evaluations">Listed ordered by FormKey.</param>
    public static CsvTable Leftovers(
        World world, IEnumerable<LeftoverEvaluation> evaluations, RelocationResult relocations, ReportContext context)
    {
        var movesByTarget = relocations.Moved.ToDictionary(move => move.Evaluation.TargetIndex);
        var rows = evaluations
            .OrderBy(evaluation => world.Targets[evaluation.TargetIndex].Key.ToString(), StringComparer.Ordinal)
            .Select(evaluation => LeftoverRow(evaluation, world, context.Bases, movesByTarget.GetValueOrDefault(evaluation.TargetIndex)));
        return new CsvTable(LeftoversFileName, LeftoversHeader, [.. rows]);
    }

    private static ImmutableArray<string> LeftoverRow(LeftoverEvaluation evaluation, World world, IBaseFacts bases, Relocation? move)
    {
        var target = world.Targets[evaluation.TargetIndex];
        var surroundings = evaluation.Surroundings;
        var position = target.Transform.Position;
        var inside = evaluation.ContainingObject;
        return Row(
        [
            Text(target.Key.ToString()),
            Text(target.EditorId ?? string.Empty),
            Text(RecordNames.DescribeBase(bases, target.Base)),
            Text(DescribeBaseType(bases, target.Base)),
            Text(evaluation.Kind.ToString()),
            Text(world.SpaceNames[target.SpaceKey]),
            Text(target.CellName ?? string.Empty),
            Num(position.X), Num(position.Y), Num(position.Z),
            Num(evaluation.Radius),
            Text(inside?.FormKey.ToString() ?? string.Empty),
            Text(inside?.WinningMod.ToString() ?? string.Empty),
            .. SectorAreas.All.SelectMany(sector => new[]
            {
                Num(surroundings.Total(sector)), Num(surroundings.Removed(sector)), Text(surroundings.State(sector).ToString()),
            }),
            Num(surroundings.OccupiedCount),
            Num(surroundings.RemovedCount),
            Text(evaluation.IsRemoved ? "removed" : "kept"),
            Text(evaluation.DescribeReason()),
            .. MoveFields(move),
        ]);
    }

    private static IEnumerable<string> MoveFields(Relocation? move) => move == null
        ? [string.Empty, string.Empty, string.Empty, string.Empty, string.Empty]
        : [Num(move.To.X), Num(move.To.Y), Num(move.To.Z), Num(move.Distance), Text(move.Surface.ToString())];

    private static string DescribeBaseType(IBaseFacts bases, BaseRef? baseRef) =>
        baseRef is { } reference && bases.Of(reference) is { Resolved: true } facts ? facts.RecordTypeName! : string.Empty;
}
