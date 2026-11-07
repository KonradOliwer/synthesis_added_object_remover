using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>A navmesh triangle in the cell record that holds its navmesh.</summary>
internal sealed record NavmeshTriangleInCell(RecordKey Navmesh, RecordKey Cell, MeshTriangle Triangle);

/// <summary>The heights between a cell's water level before the cleaned mod and its winning one; a cell without water counts as water infinitely low.</summary>
internal readonly record struct WaterLine(float Lower, float Upper)
{
    /// <param name="tolerance">The water height change tolerance.</param>
    /// <returns>
    /// Null when the cell has water at neither level, or at both within the tolerance: a cell storing its worldspace's default as its
    /// own height changes its water level part but not its water line.
    /// </returns>
    public static WaterLine? Of(CellChangedByCleanedMod cell, float tolerance) =>
        (LevelOf(cell.EarlierWater, cell.DefaultWaterLevel), LevelOf(cell.CurrentWater, cell.DefaultWaterLevel)) switch
        {
            (null, null) => null,
            ({ } earlier, { } current) when MathF.Abs(earlier - current) <= tolerance => null,
            var (earlier, current) => new WaterLine(
                MathF.Min(earlier ?? float.NegativeInfinity, current ?? float.NegativeInfinity), MathF.Max(earlier ?? float.NegativeInfinity, current ?? float.NegativeInfinity)),
        };

    /// <summary>Whether something reaching from <paramref name="bottom"/> to <paramref name="top"/> lies partly between the two levels.</summary>
    public bool Crosses(float bottom, float top) => bottom < Upper && top > Lower;

    /// <returns>Null where the cell has no water: without the "has water" flag, or using a default the worldspace does not store.</returns>
    private static float? LevelOf(CellWaterCopy water, float? defaultLevel) => water.HasWater ? water.OwnLevel ?? defaultLevel : null;
}

/// <summary>
/// Whether the water level of each cell the cleaned mod last changed it in goes back. It goes back with the cell's other undone parts,
/// unless a navmesh that stays changed, or an object that stays where the cleaned mod put it (a refused placed record or a copy), lies
/// partly between the level before the cleaned mod and the cleaned mod's, or the changed-terrain rule would count its going back for an
/// object that stays; then the cell keeps the cleaned mod's level, and its other parts still go back. Any other object at the water
/// line keeps no level beyond what the changed-terrain rule counts. Immutable: <see cref="KeepAtNavmeshWaterLines"/>,
/// <see cref="KeepAtObjectWaterLines"/> and <see cref="KeepCounted"/> give a new plan.
/// </summary>
/// <remarks>Everything runs in ascending key order on one thread, so the plan does not depend on the thread count.</remarks>
internal sealed class WaterUndoPlan
{
    private readonly ImmutableArray<CellChangeJudged> _judged;

    /// <summary>The cells whose water level goes back with them, before any is kept.</summary>
    private readonly IReadOnlySet<RecordKey> _waterUndone;

    /// <summary>
    /// The cells whose water line the cleaned mod moved (a water level part it changed, whose two levels lie apart), with that line, in
    /// key order.
    /// </summary>
    private readonly ImmutableArray<(CellChangedByCleanedMod Cell, WaterLine Line)> _moved;

    private readonly IReadOnlyDictionary<RecordKey, WaterLine> _lineOf;

    /// <summary>Each cell whose water level stays, with what keeps it first and why.</summary>
    private readonly ImmutableDictionary<RecordKey, (RecordKey By, WaterKeptReason Reason)> _kept;

    /// <param name="judged">The cells judged, in key order; none while the cells are switched off.</param>
    /// <param name="changed">Every cell record the cleaned mod changed, in key order.</param>
    /// <param name="waterTolerance">The water height change tolerance.</param>
    public WaterUndoPlan(ImmutableArray<CellChangeJudged> judged, IEnumerable<CellChangedByCleanedMod> changed, float waterTolerance)
    {
        _judged = judged;
        _waterUndone = judged.Where(WritesWaterLevel).Select(cell => cell.Change.Cell).ToHashSet();
        _moved =
        [
            .. changed
                .Where(cell => cell.Parts.Any(part => part.Name.Part == CellPart.WaterLevel && part.Outcome is not PartOutcome.Unchanged))
                .Select(cell => (Cell: cell, Line: WaterLine.Of(cell, waterTolerance)))
                .Where(moved => moved.Line is not null)
                .Select(moved => (moved.Cell, moved.Line!.Value)),
        ];
        _lineOf = _moved.ToDictionary(moved => moved.Cell.Cell, moved => moved.Line);
        _kept = ImmutableDictionary<RecordKey, (RecordKey By, WaterKeptReason Reason)>.Empty;
    }

    private WaterUndoPlan(WaterUndoPlan plan, ImmutableDictionary<RecordKey, (RecordKey By, WaterKeptReason Reason)> kept)
    {
        _judged = plan._judged;
        _waterUndone = plan._waterUndone;
        _moved = plan._moved;
        _lineOf = plan._lineOf;
        _kept = kept;
    }

    /// <summary>
    /// The water lines the cleaned mod moved whose level, as the cleaned mod set it, stays, in key order of their cells. A level a later
    /// plugin set again is that plugin's, which the restore leaves alone, so its line is not among them.
    /// </summary>
    public ImmutableArray<MovedWaterLine> LinesThatStay() =>
        [
            .. _moved
                .Where(moved => !IsLevelLeftToLaterPlugin(moved.Cell) && !GoesBack(moved.Cell.Cell))
                .Select(moved => new MovedWaterLine(moved.Cell.SpaceKey, moved.Cell.Grid, moved.Line.Lower, moved.Line.Upper, moved.Cell.Cell)),
        ];

    private static bool IsLevelLeftToLaterPlugin(CellChangedByCleanedMod cell) =>
        cell.Parts.Any(part => part.Name.Part == CellPart.WaterLevel && part.Outcome is PartOutcome.LeftToLaterPlugin);

    /// <summary>Whether the patch writes the cell's water level: the cell goes back with its water level undone.</summary>
    public static bool WritesWaterLevel(CellChangeJudged cell) => cell.IsRestored && cell.Undone.Any(part => part.Name.Part == CellPart.WaterLevel);

    /// <summary>How many water levels stay although their cell goes back.</summary>
    public int KeptCount => _kept.Count;

    /// <summary>Whether a cell's water line ends as before the cleaned mod: the cleaned mod did not move it, or its level goes back.</summary>
    public bool EndsAsBefore(RecordKey cell) => !_lineOf.ContainsKey(cell) || GoesBack(cell);

    /// <summary>The plan with every water level that would go back kept where a triangle of a navmesh that stays changed lies partly between its two levels.</summary>
    /// <param name="triangles">The triangles of the navmeshes that stay changed, in key order of their navmeshes.</param>
    public WaterUndoPlan KeepAtNavmeshWaterLines(IReadOnlyList<NavmeshTriangleInCell> triangles)
    {
        var trianglesByCell = triangles.ToLookup(triangle => triangle.Cell);
        return KeepAtWaterLines(WaterKeptReason.NavmeshAtWaterLine, (cell, line) => FirstNavmeshAtWaterLine(line, trianglesByCell[cell.Cell]));
    }

    /// <summary>
    /// The plan with every water level that would go back kept where an object that stays where the cleaned mod put it lies partly
    /// between its two levels: the cleaned mod's water stays around it, as it was placed for that water.
    /// </summary>
    /// <param name="objects">The refused placed records and the copies, in key order.</param>
    public WaterUndoPlan KeepAtObjectWaterLines(IReadOnlyList<StandingBox> objects) =>
        KeepAtWaterLines(WaterKeptReason.ObjectAtWaterLine, (cell, line) => FirstObjectAtWaterLine(cell, line, objects));

    /// <summary>The plan with every water level that would go back kept where a keeper lies at its water line.</summary>
    /// <param name="findAtLine">The first keeper lying partly between the two levels of a cell's water line; null when none does.</param>
    private WaterUndoPlan KeepAtWaterLines(WaterKeptReason reason, Func<CellChangedByCleanedMod, WaterLine, RecordKey?> findAtLine) =>
        WithKept(
            _judged.Select(judged => judged.Change)
                .Where(cell => _lineOf.ContainsKey(cell.Cell))
                .Select(cell => (cell.Cell, By: findAtLine(cell, _lineOf[cell.Cell])))
                .Where(found => found.By is not null)
                .Select(found => (found.Cell, found.By!.Value)),
            reason);

    /// <summary>
    /// The plan with the water levels of the given cells kept, each for the first object given with it, where the changed-terrain rule
    /// counts their going back; a level kept before keeps its first keeper.
    /// </summary>
    public WaterUndoPlan KeepCounted(IEnumerable<(RecordKey Cell, RecordKey By)> counted) => WithKept(counted, WaterKeptReason.ForChangedTerrainCheck);

    /// <summary>The plan with the water levels of the given cells kept, each for the record of an earlier round that would be undone.</summary>
    /// <param name="reason"><see cref="WaterKeptReason.ForEarlierRound"/> or <see cref="WaterKeptReason.ForEarlierRoundTogether"/>.</param>
    public WaterUndoPlan KeepForEarlierRound(WaterKeptReason reason, IEnumerable<(RecordKey Cell, RecordKey By)> held) => WithKept(held, reason);

    /// <summary>The plan with the water levels of the given cells that would go back kept, each for the first keeper given with it.</summary>
    private WaterUndoPlan WithKept(IEnumerable<(RecordKey Cell, RecordKey By)> found, WaterKeptReason reason)
    {
        var kept = _kept.ToBuilder();
        foreach (var (cell, by) in found.Where(entry => GoesBack(entry.Cell))) kept.TryAdd(cell, (by, reason));
        return kept.Count == _kept.Count ? this : new WaterUndoPlan(this, kept.ToImmutable());
    }

    /// <summary>The cells judged, each without its water level where that stays.</summary>
    public ImmutableArray<CellChangeJudged> Judged() => [.. _judged.Select(WithWaterKept)];

    private CellChangeJudged WithWaterKept(CellChangeJudged cell)
    {
        if (!_kept.TryGetValue(cell.Change.Cell, out var kept)) return cell;
        ImmutableArray<PartChange> undone = [.. cell.Undone.Where(part => part.Name.Part != CellPart.WaterLevel)];
        return cell with { Undone = undone, WaterKeptBy = kept.By, IsRestored = !undone.IsEmpty, WaterKeptFor = kept.Reason };
    }

    private bool GoesBack(RecordKey cell) => _waterUndone.Contains(cell) && !_kept.ContainsKey(cell);

    /// <param name="triangles">The triangles of the navmeshes that stay changed held in the cell.</param>
    private static RecordKey? FirstNavmeshAtWaterLine(WaterLine line, IEnumerable<NavmeshTriangleInCell> triangles) =>
        triangles
            .Where(triangle => line.Crosses(triangle.Triangle.Bounds.Min.Z, triangle.Triangle.Bounds.Max.Z))
            .Select(triangle => (RecordKey?)triangle.Navmesh)
            .FirstOrDefault();

    /// <summary>
    /// The first object, in key order, in the cell's space that lies partly between the two levels: one whose box reaches into an
    /// exterior cell's square seen from above, or any one in an interior.
    /// </summary>
    private static RecordKey? FirstObjectAtWaterLine(CellChangedByCleanedMod cell, WaterLine line, IEnumerable<StandingBox> objects) =>
        objects
            .Where(standing => standing.SpaceKey == cell.SpaceKey
                && line.Crosses(standing.Bounds.Min.Z, standing.Bounds.Max.Z)
                && (cell.Grid is not { } grid || ExteriorGrid.ReachesInto(standing.Bounds, grid)))
            .Select(standing => (RecordKey?)standing.Key)
            .FirstOrDefault();
}
