using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>A world grid point whose height goes back, with its height now and once written, in game units.</summary>
internal readonly record struct RestoredGridPoint(WorldGridPoint Point, float Current, float Planned);

/// <summary>A grid point whose ground ends more than the height tolerance from its height before the cleaned mod.</summary>
/// <param name="Land">The LAND record holding it; null for one the cleaned mod does not override.</param>
internal readonly record struct GroundThatStaysChanged(ExteriorCell Cell, RecordKey? Land);

/// <summary>
/// Which heights, normals, vertex colours, quarter textures and rests of the LAND records the cleaned mod changed go back, and the
/// heights written. Which points go back is chosen by <see cref="TerrainPointSelection"/>, the records holding a point on a cell edge
/// are checked by <see cref="TerrainEdgeChecks"/> and the heights written are solved by <see cref="TerrainHeightSolver"/>. A colour on
/// a cell edge goes back in every record holding it or in none. A cell whose heights the height map cannot store keeps all of its
/// own. Its outcome does not change: <see cref="Protect"/> gives a new plan.
/// </summary>
internal sealed partial class TerrainUndoPlan
{
    private readonly TerrainUndoLands _lands;

    /// <summary>Each protected point with the first reason it was protected for.</summary>
    private readonly ImmutableDictionary<LandGridPoint, TerrainKeptReason> _protected;

    /// <summary>The plan this one protects more points of, until this one's outcome is computed; null for the first plan.</summary>
    private TerrainUndoPlan? _before;

    private readonly ComputedOnce<Outcome> _outcome;

    private readonly TerrainEdgeChecks _edges;

    private readonly TerrainPointSelection _points;

    private readonly TerrainHeightSolver _heights;

    /// <param name="lands">In (worldspace, x, y) order of their cells.</param>
    /// <param name="readHeights">A cell's heights before the cleaned mod and at the winning version, for cells whose facts carry none.</param>
    /// <param name="heightTolerance">Heights closer than this count as equal.</param>
    /// <param name="blendDistance">In game units; restored points closer than this to a point that keeps a change are blended into it.</param>
    public TerrainUndoPlan(IReadOnlyList<LandAreasJudged> lands, Func<ExteriorCell, LandHeights> readHeights, float heightTolerance, float blendDistance)
        : this(new TerrainUndoLands(lands, readHeights, heightTolerance, (int)MathF.Floor(blendDistance / TerrainGrid.VertexSpacing)), ImmutableDictionary<LandGridPoint, TerrainKeptReason>.Empty, before: null)
    {
    }

    private TerrainUndoPlan(TerrainUndoLands lands, ImmutableDictionary<LandGridPoint, TerrainKeptReason> protectedPoints, TerrainUndoPlan? before)
    {
        _lands = lands;
        _protected = protectedPoints;
        _before = before;
        _outcome = new ComputedOnce<Outcome>(Compute);
        _edges = new TerrainEdgeChecks(lands);
        _points = new TerrainPointSelection(lands, protectedPoints, _edges);
        _heights = new TerrainHeightSolver(lands);
    }

    /// <summary>The plan with the given points kept at their current height too; a point protected before keeps its first reason.</summary>
    public TerrainUndoPlan Protect(TerrainKeptReason reason, IEnumerable<LandGridPoint> points)
    {
        var protectedPoints = _protected.ToBuilder();
        foreach (var point in points) protectedPoints.TryAdd(point, reason);
        return new TerrainUndoPlan(_lands, protectedPoints.ToImmutable(), this);
    }

    /// <summary>How many grid points are protected.</summary>
    public int ProtectedCount => _protected.Count;

    /// <summary>The winning heights of the cell's LAND record; null when the plan holds none or the winner has no height map.</summary>
    public float[]? CurrentHeightsOf(ExteriorCell cell) => _lands.ByCell.TryGetValue(cell, out var land) ? _lands.HeightsOf(land.Change).Current : null;

    /// <summary>The heights of the cell's LAND record once written, or its winning heights where none goes back; null when the plan holds none.</summary>
    public float[]? PlannedHeightsOf(ExteriorCell cell) => FinalHeightsOf(cell, _outcome.Value);

    /// <summary>Whether the point's height goes back.</summary>
    public bool Restores(LandGridPoint point) => _outcome.Value.Restored.Contains(WorldGridPoint.Of(point));

    /// <summary>The cells of the LAND records with a point whose height goes back, in no particular order.</summary>
    public IEnumerable<ExteriorCell> CellsWithRestoredPoints() => _outcome.Value.Final.Keys;

    /// <summary>Each world grid point whose height goes back, with its height now and once written, in the order of the cells holding them.</summary>
    public IEnumerable<RestoredGridPoint> RestoredPoints()
    {
        var outcome = _outcome.Value;
        return _lands.All
            .SelectMany(land => _lands.Candidates[land.Change.Cell].Points.Select(point => WorldGridPoint.Of(new LandGridPoint(land.Change.Cell, point))))
            .Where(outcome.Restored.Contains)
            .Distinct()
            .Select(world => HeightsAt(world, outcome));
    }

    /// <remarks>Every record holding a restored point gets the same height, so the first one's serves.</remarks>
    private RestoredGridPoint HeightsAt(WorldGridPoint world, Outcome outcome)
    {
        var holder = world.Holders()[0];
        return new RestoredGridPoint(world, _lands.HeightsOf(holder.Cell).Current![holder.Point], outcome.Final[holder.Cell][holder.Point]);
    }

    /// <summary>The points that would go back in the cells whose heights with them could not be stored, in the order of their cells.</summary>
    public IEnumerable<LandGridPoint> PointsOfCellsTooSteepToStore()
    {
        var tooSteep = _outcome.Value.TooSteep;
        return _lands.All.Where(land => tooSteep.Contains(land.Change.Cell))
            .SelectMany(land => _lands.Candidates[land.Change.Cell].Points.Select(point => new LandGridPoint(land.Change.Cell, point)));
    }

    /// <summary>
    /// The first grid point, in the order of the grid under the rectangle seen from above, that the plan does not restore and whose
    /// winning height lies more than the height tolerance from its height before the cleaned mod: ground the plan keeps as the
    /// cleaned mod made it, or ground a later plugin changed. A point the plan restores never counts, blended or not. Null when there
    /// is none.
    /// </summary>
    public GroundThatStaysChanged? GroundThatStaysChangedUnder(RecordKey landWorldspace, (Vector2 Min, Vector2 Max) area)
    {
        foreach (var under in TerrainGrid.VerticesUnder(area))
        {
            var cell = new ExteriorCell(landWorldspace, under.CellX, under.CellY);
            if (under.VertexIndices.Any(point => StaysChangedAt(new LandGridPoint(cell, point))))
            {
                return new GroundThatStaysChanged(cell, _lands.ByCell.GetValueOrDefault(cell)?.Change.LandKey);
            }
        }
        return null;
    }

    /// <summary>Each LAND record with what goes back, in the order given.</summary>
    public ImmutableArray<LandChangeJudged> Judged() => [.. _lands.All.Select(Judge)];

    private bool StaysChangedAt(LandGridPoint point)
    {
        if (Restores(point)) return false;
        var heights = _lands.ByCell.TryGetValue(point.Cell, out var land) ? land.Change.Heights : _lands.ReadHeights(point.Cell);
        return heights is not null && HeightGridComparison.DiffersAt(heights.Earlier, heights.Current, point.Point, _lands.HeightTolerance);
    }

    private LandChangeJudged Judge(LandAreasJudged land)
    {
        var change = land.Change;
        if (land.Never is { } never) return LandOutcomes.NeverRestored(change, never);

        var outcome = _outcome.Value;
        var candidates = _lands.Candidates[change.Cell];
        var heights = PointSet.Of(candidates.Points.Where(point => outcome.Restored.Contains(WorldGridPoint.Of(new LandGridPoint(change.Cell, point)))));
        var colours = PointSet.Of(_lands.ColourCandidates[change.Cell].Points.Where(point => _edges.ColourGoesBack(new LandGridPoint(change.Cell, point))));
        var textures = QuarterSet.Of(change.UndoQuarters.Quarters.Where(quarter => land.Quarters[quarter].Verdict == AreaVerdict.Cleared));
        var rest = change.UndoRest && land.Quarters.All(quarter => quarter.Verdict == AreaVerdict.Cleared);
        var final = outcome.Final.GetValueOrDefault(change.Cell);
        return LandOutcomes.Judged(
            change,
            land.Quarters,
            heights,
            colours,
            textures,
            rest,
            _points.KeptPoints(land, candidates, heights, outcome.TooSteep),
            final,
            final is null ? CellEdgeHeights.Unknown : BeyondOf(change.Cell, outcome));
    }

    /// <summary>The final heights of the neighbouring cells next to the cell's edges; a side is unknown where the plan holds no heights of that cell.</summary>
    private CellEdgeHeights BeyondOf(ExteriorCell cell, Outcome outcome) => CellEdgeHeights.Beside(
        FinalHeightsOf(cell with { X = cell.X - 1 }, outcome),
        FinalHeightsOf(cell with { X = cell.X + 1 }, outcome),
        FinalHeightsOf(cell with { Y = cell.Y - 1 }, outcome),
        FinalHeightsOf(cell with { Y = cell.Y + 1 }, outcome));

    private float[]? FinalHeightsOf(ExteriorCell cell, Outcome outcome) => outcome.Final.GetValueOrDefault(cell) ?? CurrentHeightsOf(cell);
}
