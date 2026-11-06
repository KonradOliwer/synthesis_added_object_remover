using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>A world grid point whose height goes back, with its height now and once written, in game units.</summary>
internal readonly record struct RestoredGridPoint(WorldGridPoint Point, float Current, float Planned);

/// <summary>
/// Which heights, normals, vertex colours, quarter textures and rests of the LAND records the cleaned mod changed go back, and the
/// heights written. Which points go back is chosen by <see cref="TerrainPointSelection"/>, the records holding a point on a cell edge
/// are checked by <see cref="TerrainEdgeChecks"/> and the heights written are solved by <see cref="TerrainHeightSolver"/>. A colour on
/// a cell edge goes back in every record holding it or in none. A cell whose heights the height map cannot store keeps all of its
/// own. A quarter whose textures going back the changed-terrain rule counts for an object that stays keeps its texture layers. Its outcome does not change:
/// <see cref="Protect"/> and <see cref="KeepTextures"/> give a new plan.
/// </summary>
internal sealed partial class TerrainUndoPlan
{
    private readonly TerrainUndoLands _lands;

    /// <summary>Each protected point with the first reason it was protected for.</summary>
    private readonly ImmutableDictionary<LandGridPoint, TerrainKeptReason> _protected;

    /// <summary>The quarters whose texture layers stay for an object that stays: the changed-terrain rule counts their going back.</summary>
    private readonly ImmutableHashSet<(ExteriorCell Cell, int Quarter)> _texturesKept;

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
        : this(
            new TerrainUndoLands(lands, readHeights, heightTolerance, (int)MathF.Floor(blendDistance / TerrainGrid.VertexSpacing)),
            ImmutableDictionary<LandGridPoint, TerrainKeptReason>.Empty,
            [],
            before: null)
    {
    }

    private TerrainUndoPlan(
        TerrainUndoLands lands, ImmutableDictionary<LandGridPoint, TerrainKeptReason> protectedPoints, ImmutableHashSet<(ExteriorCell Cell, int Quarter)> texturesKept, TerrainUndoPlan? before)
    {
        _lands = lands;
        _protected = protectedPoints;
        _texturesKept = texturesKept;
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
        return new TerrainUndoPlan(_lands, protectedPoints.ToImmutable(), _texturesKept, this);
    }

    /// <summary>The plan with the texture layers of the given quarters kept too, for objects that stay for which the changed-terrain rule counts their going back.</summary>
    public TerrainUndoPlan KeepTextures(IEnumerable<(ExteriorCell Cell, int Quarter)> quarters)
    {
        var kept = _texturesKept.Union(quarters.Where(quarter => _lands.ByCell.ContainsKey(quarter.Cell)));
        return kept.Count == _texturesKept.Count ? this : new TerrainUndoPlan(_lands, _protected, kept, this);
    }

    /// <summary>How many grid points are protected.</summary>
    public int ProtectedCount => _protected.Count;

    /// <summary>How many quarters keep their texture layers for an object that stays.</summary>
    public int TexturesKeptCount => _texturesKept.Count;

    /// <summary>The winning heights of the cell's LAND record; null when the plan holds none or the winner has no height map.</summary>
    public float[]? CurrentHeightsOf(ExteriorCell cell) => _lands.ByCell.TryGetValue(cell, out var land) ? _lands.HeightsOf(land.Change).Current : null;

    /// <summary>The heights of the cell's LAND record once written, or its winning heights where none goes back; null when the plan holds none.</summary>
    public float[]? PlannedHeightsOf(ExteriorCell cell) => FinalHeightsOf(cell, _outcome.Value);

    /// <summary>A cell's heights before the cleaned mod, and as the plan ends them where it plans the cell, else as they are.</summary>
    public LandHeights GroundBeforeAndAfter(ExteriorCell cell, LandHeights ground) => ground with { Current = PlannedHeightsOf(cell) ?? ground.Current };

    /// <summary>The cells of the LAND records with a point whose height goes back, in no particular order.</summary>
    public IEnumerable<ExteriorCell> CellsWithRestoredPoints() => _outcome.Value.Final.Keys;

    /// <summary>Each world grid point whose height goes back, with its height now and once written, in the order of the cells holding them.</summary>
    public IEnumerable<RestoredGridPoint> RestoredPoints()
    {
        var outcome = _outcome.Value;
        return _lands.Planned
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
        return _lands.Planned.Where(land => tooSteep.Contains(land.Change.Cell))
            .SelectMany(land => _lands.Candidates[land.Change.Cell].Points.Select(point => new LandGridPoint(land.Change.Cell, point)));
    }

    /// <summary>Each LAND record with what goes back, in the order given.</summary>
    public ImmutableArray<LandChangeJudged> Judged() => [.. _lands.All.Select(Judge)];

    private LandChangeJudged Judge(LandAreasJudged land)
    {
        var change = land.Change;
        if (land.Never is { } never) return LandOutcomes.NeverRestored(change, never);

        var outcome = _outcome.Value;
        var candidates = _lands.Candidates[change.Cell];
        var heights = PointSet.Of(candidates.Points.Where(point => outcome.Restored.Contains(WorldGridPoint.Of(new LandGridPoint(change.Cell, point)))));
        var colours = PointSet.Of(_lands.ColourCandidates[change.Cell].Points.Where(point => _edges.ColourGoesBack(new LandGridPoint(change.Cell, point))));
        var texturesKept = QuarterSet.Of(change.UndoQuarters.Quarters.Where(quarter => _texturesKept.Contains((change.Cell, quarter))));
        var textures = QuarterSet.Of(change.UndoQuarters.Quarters.Where(quarter => land.Quarters[quarter].Verdict == AreaVerdict.Cleared && !texturesKept.Contains(quarter)));
        var rest = change.UndoRest && land.Quarters.All(quarter => quarter.Verdict == AreaVerdict.Cleared);
        var final = outcome.Final.GetValueOrDefault(change.Cell);
        return LandOutcomes.Judged(
            change,
            land.Quarters,
            heights,
            colours,
            textures,
            texturesKept,
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
