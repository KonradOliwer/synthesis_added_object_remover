using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>A world grid point whose height goes back, with its height now and once written, in game units.</summary>
internal readonly record struct RestoredGridPoint(WorldGridPoint Point, float Current, float Planned);

/// <summary>A terrain square whose ground ends more than the height tolerance from the ground of the world without the cleaned mod.</summary>
/// <param name="Land">The LAND record holding it, which the cleaned mod overrides.</param>
internal readonly record struct GroundThatStaysChanged(ExteriorCell Cell, RecordKey Land, GroundGap Gap);

/// <summary>
/// Which heights, normals, vertex colours, quarter textures and rests of the LAND records the cleaned mod changed go back, and the
/// heights written. Which points go back is chosen by <see cref="TerrainPointSelection"/>, the records holding a point on a cell edge
/// are checked by <see cref="TerrainEdgeChecks"/> and the heights written are solved by <see cref="TerrainHeightSolver"/>. A colour on
/// a cell edge goes back in every record holding it or in none. A cell whose heights the height map cannot store keeps all of its
/// own. A quarter whose ground the changed-terrain step checks around an object that stays keeps its texture layers. Its outcome does not change:
/// <see cref="Protect"/> and <see cref="KeepTextures"/> give a new plan.
/// </summary>
internal sealed partial class TerrainUndoPlan
{
    private readonly TerrainUndoLands _lands;

    /// <summary>Each protected point with the first reason it was protected for.</summary>
    private readonly ImmutableDictionary<LandGridPoint, TerrainKeptReason> _protected;

    /// <summary>The quarters whose texture layers stay for an object that stays: the changed-terrain step checks their ground around it.</summary>
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

    /// <summary>The plan with the texture layers of the given quarters kept too, for objects that stay whose ground the changed-terrain step checks there.</summary>
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

    /// <summary>The ground of the cell in the world without the cleaned mod (<see cref="TerrainUndoLands.HeightsWithoutCleanedMod"/>); null when the plan holds no such heights.</summary>
    public float[]? HeightsWithoutCleanedModOf(ExteriorCell cell) => _lands.HeightsWithoutCleanedMod.GetValueOrDefault(cell);

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
    /// The first terrain square, in the order of the squares under the footprint, where the ground under the footprint stays changed
    /// through the cleaned mod: ground the plan keeps as the cleaned mod made it, in a LAND record the cleaned mod overrides, that
    /// rises into the height range or drops away from it. The ground is compared with the world without the cleaned mod
    /// (<see cref="TerrainUndoLands.HeightsWithoutCleanedMod"/>), so ground only an unrelated plugin changed never counts. Each point is judged at the
    /// height the patch leaves there: a point the plan restores at the height it writes, blended or not. Null when there is none.
    /// </summary>
    /// <param name="footprint">The rectangle a box covers, seen from above.</param>
    /// <param name="heights">The lowest and highest height of the box.</param>
    public GroundThatStaysChanged? GroundThatStaysChangedUnder(RecordKey landWorldspace, (Vector2 Min, Vector2 Max) footprint, (float Min, float Max) heights)
    {
        foreach (var part in TerrainGrid.SquaresUnder(footprint))
        {
            var cell = new ExteriorCell(landWorldspace, part.CellX, part.CellY);
            if (GapWhereGroundStaysChanged(cell, part, heights) is { } gap) return new GroundThatStaysChanged(cell, _lands.ByCell[cell].Change.LandKey, gap);
        }
        return null;
    }

    /// <summary>Each LAND record with what goes back, in the order given.</summary>
    public ImmutableArray<LandChangeJudged> Judged() => [.. _lands.All.Select(Judge)];

    /// <summary>
    /// The gap when the ground under the part of the footprint in the square, in the world without the cleaned mod and as it ends,
    /// differs by more than the tolerance and comes within the tolerance of the height range. A grid next to a missing one has no
    /// height to compare, so the square counts once a corner of the cleaned mod's differs.
    /// </summary>
    /// <remarks>
    /// The game draws each square as two triangles. Which diagonal it splits the square along is not known, so the part counts when
    /// it does on either split. On each split the surface and its change are linear on each triangle, so their extremes over the
    /// part lie at its bounding points; the part counts when one of them changes beyond the tolerance and the heights between the
    /// earlier and final surface over them reach the range.
    /// </remarks>
    /// <returns>The gap, with the largest difference over the splits on which the part counts; null when the ground there does not stay changed.</returns>
    private GroundGap? GapWhereGroundStaysChanged(ExteriorCell cell, SquarePart part, (float Min, float Max) range)
    {
        if (!_lands.ByCell.TryGetValue(cell, out var land) || land.Change.Heights is not { } heights) return null;

        var tolerance = _lands.HeightTolerance;
        if (heights is not { Earlier: not null, Current: { } current })
        {
            return AnyCornerDiffers(cell, part, heights) ? new GroundGap(Largest: null, tolerance) : null;
        }

        var without = _lands.HeightsWithoutCleanedMod[cell];
        var final = PlannedHeightsOf(cell) ?? current;
        return Enum.GetValues<SquareSplit>().Select(split => LargestDifferenceWhereItStays(split, part, without, final, range)).Max() is { } largest
            ? new GroundGap(largest, tolerance)
            : null;
    }

    /// <summary>
    /// Whether a corner of the square that the cleaned mod changed and the plan does not restore differs, its cell having a height map
    /// in only one of the two versions. A corner the cleaned mod did not change is the ground of the world without the cleaned mod.
    /// </summary>
    private bool AnyCornerDiffers(ExteriorCell cell, SquarePart part, LandHeights heights) =>
        TerrainGrid.CornersOf(part.FirstCorner).Any(point =>
            _lands.CleanedModShaped(cell, point) && !Restores(new LandGridPoint(cell, point)) && HeightGridComparison.DiffersAt(heights.Earlier, heights.Current, point, _lands.HeightTolerance));

    /// <returns>The largest difference between the surface of the world without the cleaned mod and the final surface over the part on the split when the part counts on it; null otherwise.</returns>
    private float? LargestDifferenceWhereItStays(SquareSplit split, SquarePart part, float[] without, float[] final, (float Min, float Max) range)
    {
        var surfaces = TerrainGrid.BoundingPointsOnTriangles(part, split)
            .Select(point => (Earlier: TerrainGrid.HeightOnTriangles(without, part.FirstCorner, point, split), Final: TerrainGrid.HeightOnTriangles(final, part.FirstCorner, point, split)))
            .ToList();
        var largest = surfaces.Max(surface => MathF.Abs(surface.Final - surface.Earlier));
        if (largest <= _lands.HeightTolerance) return null;
        var low = surfaces.Min(surface => MathF.Min(surface.Earlier, surface.Final));
        var high = surfaces.Max(surface => MathF.Max(surface.Earlier, surface.Final));
        return low - _lands.HeightTolerance <= range.Max && high + _lands.HeightTolerance >= range.Min ? largest : null;
    }

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
