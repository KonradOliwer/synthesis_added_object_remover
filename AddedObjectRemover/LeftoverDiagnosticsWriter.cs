using static AddedObjectRemover.CsvFile;

namespace AddedObjectRemover;

/// <summary>
/// Optional leftover-invisible-objects.csv: one row per invisible target object checked by the
/// leftover invisible objects step, with the other mod's object it sits inside, the ground area of
/// the target objects around it and how much of it was removed per direction, the decision and,
/// when it was moved, where to.
/// </summary>
internal static class LeftoverDiagnosticsWriter
{
    public const string FileName = "leftover-invisible-objects.csv";

    private static readonly string[] Header =
    [
        "formKey", "editorId", "base", "baseType", "invisibleType", "space", "cell", "x", "y", "z",
        "radius", "insideObject", "insideObjectPlugin",
        .. SectorAreas.All.SelectMany(sector => new[] { $"{sector}Total", $"{sector}Removed", $"{sector}State" }),
        "occupiedDirections", "removedDirections", "decision", "reason",
        "movedToX", "movedToY", "movedToZ", "moveDistance", "movedOnto",
    ];

    /// <returns>The path written to.</returns>
    public static string Write(
        string folder,
        World world,
        IBaseFacts bases,
        IReadOnlyList<LeftoverEvaluation> evaluations,
        RelocationResult relocations)
    {
        var path = Path.Combine(folder, FileName);
        var movesByTarget = relocations.Moved.ToDictionary(move => move.Evaluation.TargetIndex);
        var rows = evaluations
            .OrderBy(evaluation => world.Targets[evaluation.TargetIndex].Key.ToString(), StringComparer.Ordinal)
            .Select(evaluation => FormatRow(evaluation, world, bases, movesByTarget.GetValueOrDefault(evaluation.TargetIndex)));
        CsvFile.Write(path, Header, rows);
        return path;
    }

    private static IEnumerable<string> FormatRow(LeftoverEvaluation evaluation, World world, IBaseFacts bases, Relocation? move)
    {
        var target = world.Targets[evaluation.TargetIndex];
        var surroundings = evaluation.Surroundings;
        var position = target.Transform.Position;
        var inside = evaluation.ContainingObject;
        return
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
            .. FormatMove(move),
        ];
    }

    private static IEnumerable<string> FormatMove(Relocation? move) => move == null
        ? [string.Empty, string.Empty, string.Empty, string.Empty, string.Empty]
        : [Num(move.To.X), Num(move.To.Y), Num(move.To.Z), Num(move.Distance), Text(move.Surface.ToString())];

    private static string DescribeBaseType(IBaseFacts bases, BaseRef? baseRef) =>
        baseRef is { } reference && bases.Of(reference) is { Resolved: true } facts ? facts.RecordTypeName! : string.Empty;
}
