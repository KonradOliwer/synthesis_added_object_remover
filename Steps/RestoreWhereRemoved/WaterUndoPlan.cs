using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>A navmesh triangle in the cell record that holds its navmesh.</summary>
internal sealed record NavmeshTriangleInCell(RecordKey Navmesh, RecordKey Cell, MeshTriangle Triangle);

/// <summary>An area the changed-terrain step checks around an object that stays, with what keeps a water level the area reaches and why.</summary>
internal sealed record WaterCheckArea(RecordKey Space, Box Area, RecordKey Keeper, WaterKeptReason Reason);

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
/// unless an object or a navmesh that stays lies partly between the level before the cleaned mod and the cleaned mod's, or the
/// changed-terrain step checks the cell's ground around an object that stays and counts water changes; then the cell keeps the cleaned
/// mod's level, and its other parts still go back. Immutable: <see cref="KeepAtWaterLines"/> and <see cref="KeepAroundObjectsThatStay"/>
/// give a new plan.
/// </summary>
/// <remarks>Everything runs in ascending key order on one thread, so the plan does not depend on the thread count.</remarks>
internal sealed class WaterUndoPlan
{
    private readonly ImmutableArray<CellChangeJudged> _judged;

    /// <summary>The cells whose water level goes back with them, before any is kept.</summary>
    private readonly IReadOnlySet<RecordKey> _waterUndone;

    /// <summary>
    /// The cells whose water line the cleaned mod moved (a water level part it changed, whose two levels lie apart), with that line,
    /// by their space, in key order.
    /// </summary>
    private readonly ILookup<RecordKey, (CellChangedByCleanedMod Cell, WaterLine Line)> _waterLineMoved;

    private readonly IReadOnlyDictionary<RecordKey, WaterLine> _lineOf;

    /// <summary>Each cell whose water level stays, with what keeps it first and why.</summary>
    private readonly ImmutableDictionary<RecordKey, (RecordKey By, WaterKeptReason Reason)> _kept;

    /// <param name="judged">The cells judged, in key order; none while the cells are switched off.</param>
    /// <param name="changed">Every cell record the cleaned mod changed, in key order.</param>
    /// <param name="waterTolerance">The water height change tolerance.</param>
    public WaterUndoPlan(ImmutableArray<CellChangeJudged> judged, IEnumerable<CellChangedByCleanedMod> changed, float waterTolerance)
    {
        _judged = judged;
        _waterUndone = judged.Where(cell => cell.IsRestored && cell.Undone.Any(part => part.Name.Part == CellPart.WaterLevel)).Select(cell => cell.Change.Cell).ToHashSet();
        _waterLineMoved = changed
            .Where(cell => cell.Parts.Any(part => part.Name.Part == CellPart.WaterLevel && part.Outcome is not PartOutcome.Unchanged))
            .Select(cell => (Cell: cell, Line: WaterLine.Of(cell, waterTolerance)))
            .Where(moved => moved.Line is not null)
            .Select(moved => (moved.Cell, moved.Line!.Value))
            .ToLookup(moved => moved.Cell.SpaceKey);
        _lineOf = _waterLineMoved.SelectMany(cells => cells).ToDictionary(moved => moved.Cell.Cell, moved => moved.Line);
        _kept = ImmutableDictionary<RecordKey, (RecordKey By, WaterKeptReason Reason)>.Empty;
    }

    private WaterUndoPlan(WaterUndoPlan plan, ImmutableDictionary<RecordKey, (RecordKey By, WaterKeptReason Reason)> kept)
    {
        _judged = plan._judged;
        _waterUndone = plan._waterUndone;
        _waterLineMoved = plan._waterLineMoved;
        _lineOf = plan._lineOf;
        _kept = kept;
    }

    /// <summary>How many water levels stay although their cell goes back.</summary>
    public int KeptCount => _kept.Count;

    /// <summary>Whether a cell's water line ends as before the cleaned mod: the cleaned mod did not move it, or its level goes back.</summary>
    public bool EndsAsBefore(RecordKey cell) => !_lineOf.ContainsKey(cell) || GoesBack(cell);

    /// <summary>
    /// The first cell, in key order, whose water line stays moved and is crossed by the box: an exterior cell whose square the box
    /// reaches into, or the interior the box is in; null when there is none.
    /// </summary>
    public RecordKey? CellWithChangedWaterLineIn(RecordKey spaceKey, Box box) =>
        _waterLineMoved[spaceKey]
            .Where(moved => !GoesBack(moved.Cell.Cell) && Covers(moved.Cell, box) && moved.Line.Crosses(box.Min.Z, box.Max.Z))
            .Select(moved => (RecordKey?)moved.Cell.Cell)
            .FirstOrDefault();

    /// <summary>The plan with every water level that would go back kept where an object or a navmesh triangle lies partly between its two levels.</summary>
    /// <param name="objects">The objects that stay, in key order.</param>
    /// <param name="triangles">The triangles of the navmeshes that stay changed, in key order of their navmeshes.</param>
    public WaterUndoPlan KeepAtWaterLines(IReadOnlyList<StandingBox> objects, IReadOnlyList<NavmeshTriangleInCell> triangles)
    {
        var kept = _kept.ToBuilder();
        var objectsByPlace = new ObjectsByPlace(objects);
        var trianglesByCell = triangles.ToLookup(triangle => triangle.Cell);
        foreach (var cell in _judged.Select(judged => judged.Change).Where(cell => GoesBack(cell.Cell) && _lineOf.ContainsKey(cell.Cell)))
        {
            if (FirstAtWaterLine(cell, _lineOf[cell.Cell], objectsByPlace, trianglesByCell[cell.Cell]) is { } by) kept.Add(cell.Cell, (by, WaterKeptReason.AtWaterLine));
        }
        return new WaterUndoPlan(this, kept.ToImmutable());
    }

    /// <summary>
    /// The plan with every water level that would go back kept in the cells an area reaches: the changed-terrain step checks the ground
    /// there around an object that stays and counts water changes. Each cell names the keeper of the first area in the order given.
    /// </summary>
    /// <param name="areas">The areas the step checks around each object that stays; read only when a water level would go back.</param>
    public WaterUndoPlan KeepAroundObjectsThatStay(IEnumerable<WaterCheckArea> areas)
    {
        List<CellChangedByCleanedMod> goingBack = [.. _judged.Select(judged => judged.Change).Where(cell => GoesBack(cell.Cell))];
        if (goingBack.Count == 0) return this;

        var bySpace = areas.ToLookup(area => area.Space);
        var kept = _kept.ToBuilder();
        foreach (var cell in goingBack)
        {
            if (bySpace[cell.SpaceKey].FirstOrDefault(area => Covers(cell, area.Area)) is { } found) kept.Add(cell.Cell, (found.Keeper, found.Reason));
        }
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
    private static RecordKey? FirstAtWaterLine(CellChangedByCleanedMod cell, WaterLine line, ObjectsByPlace objects, IEnumerable<NavmeshTriangleInCell> triangles)
    {
        var standing = objects.Near(cell)
            .Where(standing => Covers(cell, standing.Bounds) && line.Crosses(standing.Bounds.Min.Z, standing.Bounds.Max.Z))
            .Select(standing => (RecordKey?)standing.Key);
        var navmeshes = triangles
            .Where(triangle => line.Crosses(triangle.Triangle.Bounds.Min.Z, triangle.Triangle.Bounds.Max.Z))
            .Select(triangle => (RecordKey?)triangle.Navmesh);
        return standing.Concat(navmeshes).FirstOrDefault();
    }

    /// <summary>Whether the box reaches into an exterior cell's square seen from above; an interior holds everything in its space.</summary>
    private static bool Covers(CellChangedByCleanedMod cell, Box box) =>
        cell.Grid is not var (x, y)
        || (x * ExteriorGrid.CellSize < box.Max.X && (x + 1) * ExteriorGrid.CellSize > box.Min.X
            && y * ExteriorGrid.CellSize < box.Max.Y && (y + 1) * ExteriorGrid.CellSize > box.Min.Y);

    /// <summary>The objects that stay by their space, and by each exterior cell square their bounds reach; each list in key order.</summary>
    private sealed class ObjectsByPlace(IReadOnlyList<StandingBox> objects)
    {
        private readonly ILookup<RecordKey, StandingBox> _bySpace = objects.ToLookup(standing => standing.SpaceKey);
        private readonly ILookup<(RecordKey Space, int X, int Y), StandingBox> _bySquare = objects
            .SelectMany(standing => ExteriorGrid.CellsUnder(standing.Bounds).Select(square => (Key: (standing.SpaceKey, square.X, square.Y), Standing: standing)))
            .ToLookup(entry => entry.Key, entry => entry.Standing);

        /// <summary>Every object of the cell's space for an interior; for an exterior cell, those whose bounds reach its square.</summary>
        public IEnumerable<StandingBox> Near(CellChangedByCleanedMod cell) => cell.Grid is var (x, y) ? _bySquare[(cell.SpaceKey, x, y)] : _bySpace[cell.SpaceKey];
    }
}
