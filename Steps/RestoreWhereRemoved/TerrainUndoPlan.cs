using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>A world grid point whose height goes back, with its height now and once written, in game units.</summary>
internal readonly record struct RestoredGridPoint(WorldGridPoint Point, float Current, float Planned);

/// <summary>A grid point whose ground ends more than the height tolerance from its height before the cleaned mod.</summary>
/// <param name="Land">The LAND record holding it; null for one the cleaned mod does not override.</param>
internal readonly record struct GroundThatStaysChanged(ExteriorCell Cell, RecordKey? Land);

/// <summary>
/// Which heights, normals, vertex colours, quarter textures and rests of the LAND records the cleaned mod changed go back, and the
/// heights written. A point goes back when every LAND record holding it (two or four on a cell edge) lets it: the cleaned mod is its
/// last changer there, every quarter holding it is cleared, nothing protects it, and the records held equal earlier heights and
/// store heights on the same steps; each record then gets the same height. A colour on a cell edge goes back in every record holding
/// it or in none. Restored points near a point that keeps a change are blended into it, and every planned height lies on the steps
/// its record stores, so ground that stays keeps its exact height. A cell whose heights the height map cannot store keeps all of its
/// own. Its outcome does not change: <see cref="Protect"/> gives a new plan.
/// </summary>
/// <remarks>
/// Everything runs in ascending order on one thread, so the plan does not depend on the thread count. A plan protecting more points
/// than one already computed starts from that one's outcome and recomputes only the cells near the points it takes back; the
/// outcome is the same as computing it afresh.
/// </remarks>
internal sealed class TerrainUndoPlan
{
    /// <summary>Two records whose offsets lie closer than this to a whole number of steps apart store heights on the same steps.</summary>
    private const float SameStepsTolerance = 1e-3f;

    private const float UnitsPerStep = LandHeightEncoder.UnitsPerStep;

    private readonly Lands _lands;

    /// <summary>Each protected point with the first reason it was protected for.</summary>
    private readonly ImmutableDictionary<LandGridPoint, TerrainKeptReason> _protected;

    /// <summary>The plan this one protects more points of, until this one's outcome is computed; null for the first plan.</summary>
    private TerrainUndoPlan? _before;

    private readonly ComputedOnce<Outcome> _outcome;

    /// <param name="lands">In (worldspace, x, y) order of their cells.</param>
    /// <param name="readHeights">A cell's heights before the cleaned mod and at the winning version, for cells whose facts carry none.</param>
    /// <param name="heightTolerance">Heights closer than this count as equal.</param>
    /// <param name="blendDistance">In game units; restored points closer than this to a point that keeps a change are blended into it.</param>
    public TerrainUndoPlan(IReadOnlyList<LandAreasJudged> lands, Func<ExteriorCell, LandHeights> readHeights, float heightTolerance, float blendDistance)
        : this(new Lands(lands, readHeights, heightTolerance, (int)MathF.Floor(blendDistance / TerrainGrid.VertexSpacing)), ImmutableDictionary<LandGridPoint, TerrainKeptReason>.Empty, before: null)
    {
    }

    private TerrainUndoPlan(Lands lands, ImmutableDictionary<LandGridPoint, TerrainKeptReason> protectedPoints, TerrainUndoPlan? before)
    {
        _lands = lands;
        _protected = protectedPoints;
        _before = before;
        _outcome = new ComputedOnce<Outcome>(Compute);
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
        if (land.Never is not null)
        {
            return new LandChangeJudged(change, land.Never, [], PointSet.Of([]), PointSet.Of([]), default, Rest: false, [], FinalHeights: null, CellEdgeHeights.Unknown, IsRestored: false);
        }

        var outcome = _outcome.Value;
        var candidates = _lands.Candidates[change.Cell];
        var heights = PointSet.Of(candidates.Points.Where(point => outcome.Restored.Contains(WorldGridPoint.Of(new LandGridPoint(change.Cell, point)))));
        var colours = PointSet.Of(_lands.ColourCandidates[change.Cell].Points.Where(point => ColourGoesBack(new LandGridPoint(change.Cell, point))));
        var textures = QuarterSet.Of(change.UndoQuarters.Quarters.Where(quarter => land.Quarters[quarter].Verdict == AreaVerdict.Cleared));
        var rest = change.UndoRest && land.Quarters.All(quarter => quarter.Verdict == AreaVerdict.Cleared);
        var final = outcome.Final.GetValueOrDefault(change.Cell);
        return new LandChangeJudged(
            change,
            Never: null,
            land.Quarters,
            heights,
            colours,
            textures,
            rest,
            KeptPoints(land, candidates, heights, outcome),
            final,
            final is null ? CellEdgeHeights.Unknown : BeyondOf(change.Cell, outcome),
            !heights.IsEmpty || !colours.IsEmpty || !textures.IsEmpty || rest);
    }

    /// <summary>Whether every record holding the point's world grid point lets its colour go back there.</summary>
    private bool ColourGoesBack(LandGridPoint point)
    {
        foreach (var holder in WorldGridPoint.Of(point).Holders())
        {
            if (!_lands.ColourCandidates.TryGetValue(holder.Cell, out var colours) || !colours.Contains(holder.Point)) return false;
        }
        return true;
    }

    private Outcome Compute()
    {
        var work = StartFromAnOutcomeBefore() ?? StartAfresh();
        var outcome = TakeBackCellsTooSteepToStore(work);
        _before = null;
        return outcome;
    }

    /// <summary>Every world point that goes back by the protection alone, with every cell's heights.</summary>
    private Work StartAfresh()
    {
        var restored = new HashSet<WorldGridPoint>();
        foreach (var land in _lands.All)
        {
            foreach (var point in _lands.Candidates[land.Change.Cell].Points)
            {
                var world = WorldGridPoint.Of(new LandGridPoint(land.Change.Cell, point));
                if (GoesBack(world)) restored.Add(world);
            }
        }
        var final = new Dictionary<ExteriorCell, float[]>();
        var cells = _lands.All.Select(land => land.Change.Cell).ToHashSet();
        foreach (var cell in cells) UpdateFinalHeights(cell, restored, final);
        return new Work(restored, final, cells);
    }

    /// <summary>
    /// The outcome of the nearest plan before this one that is computed, without the points this plan protects besides; null when
    /// there is none, or when this plan does not protect every point that plan's cells too steep to store would restore.
    /// </summary>
    /// <remarks>
    /// Those points then keep their cells out of the points that go back, as the steep cells did there, so the points that go back by
    /// the protection alone are that plan's minus those next to a newly protected point, and only the cells near them change.
    /// </remarks>
    private Work? StartFromAnOutcomeBefore()
    {
        var before = _before;
        while (before is not null && !before._outcome.IsComputed) before = before._before;
        if (before is null) return null;
        var earlier = before._outcome.Value;
        if (!earlier.TooSteep.All(cell => _lands.Candidates[cell].Points.All(point => _protected.ContainsKey(new LandGridPoint(cell, point))))) return null;

        var restored = new HashSet<WorldGridPoint>(earlier.Restored);
        var takenBack = new List<WorldGridPoint>();
        foreach (var point in _protected.Keys)
        {
            var world = WorldGridPoint.Of(point);
            if (!before._protected.ContainsKey(point) && restored.Remove(world)) takenBack.Add(world);
        }
        var final = new Dictionary<ExteriorCell, float[]>(earlier.Final);
        var changed = CellsNear(takenBack);
        foreach (var cell in changed) UpdateFinalHeights(cell, restored, final);
        return new Work(restored, final, changed);
    }

    /// <remarks>Each round takes back every point of at least one more cell, which then holds none, so the rounds end after at most one more than there are cells.</remarks>
    private Outcome TakeBackCellsTooSteepToStore(Work work)
    {
        var tooSteep = new HashSet<ExteriorCell>();
        var changed = work.Changed;
        while (true)
        {
            var steep = changed.Where(cell => work.Final.TryGetValue(cell, out var heights) && !LandHeightEncoder.CanStore(heights)).ToList();
            if (steep.Count == 0) return new Outcome(work.Restored, work.Final, tooSteep);
            tooSteep.UnionWith(steep);
            var takenBack = new List<WorldGridPoint>();
            foreach (var cell in steep)
            {
                foreach (var point in _lands.Candidates[cell].Points)
                {
                    var world = WorldGridPoint.Of(new LandGridPoint(cell, point));
                    if (work.Restored.Remove(world)) takenBack.Add(world);
                }
            }
            changed = CellsNear(takenBack);
            foreach (var cell in changed) UpdateFinalHeights(cell, work.Restored, work.Final);
        }
    }

    /// <summary>The cells holding a point within the blend distance of one of the points, whose final heights depend on them.</summary>
    private HashSet<ExteriorCell> CellsNear(List<WorldGridPoint> points)
    {
        var cells = new HashSet<ExteriorCell>();
        foreach (var point in points)
        {
            foreach (var holder in point.Holders()) cells.Add(holder.Cell);
            foreach (var step in _lands.BlendSteps)
            {
                foreach (var holder in point.StepBy(step).Holders()) cells.Add(holder.Cell);
            }
        }
        return cells;
    }

    /// <summary>Sets the cell's heights with its restored points planned and the others at their current height; removes them when none goes back.</summary>
    private void UpdateFinalHeights(ExteriorCell cell, IReadOnlySet<WorldGridPoint> restored, Dictionary<ExteriorCell, float[]> final)
    {
        if (!_lands.ByCell.TryGetValue(cell, out var land)) return;
        float[]? heights = null;
        foreach (var point in _lands.Candidates[cell].Points)
        {
            var world = WorldGridPoint.Of(new LandGridPoint(cell, point));
            if (!restored.Contains(world)) continue;
            heights ??= [.. _lands.HeightsOf(land.Change).Current!];
            heights[point] = PlannedHeight(world, restored);
        }
        if (heights is null) final.Remove(cell);
        else final[cell] = heights;
    }

    /// <summary>
    /// The earlier height, moved towards the current one by 1 − k/(n+1), where k is the grid distance to the nearest point that keeps
    /// a change and n the blend distance in grid steps; the earlier height itself when no such point is within n, or a point nobody
    /// changed lies nearer. Then put on the nearest step the record stores, counted from its winning first point.
    /// </summary>
    /// <remarks>
    /// Taken from the first record holding the point, so every record holding it gets the same height. Every height a record stores
    /// lies whole steps from its first point's, so on these steps the winning heights the plan keeps are stored exactly, and so are
    /// the planned ones, in each record holding them, since the records' steps agree.
    /// </remarks>
    private float PlannedHeight(WorldGridPoint world, IReadOnlySet<WorldGridPoint> restored)
    {
        var holder = world.Holders()[0];
        var heights = _lands.HeightsOf(holder.Cell);
        var (earlier, current) = (heights.Earlier![holder.Point], heights.Current![holder.Point]);
        var blended = DistanceToChangeKept(world, restored) is { } distance ? earlier + (current - earlier) * (1f - distance / (_lands.BlendDistance + 1)) : earlier;
        return LandHeightEncoder.NearestOnTheStepsOf(blended, heights.Current[0]);
    }

    /// <summary>
    /// The distance of the nearest point that keeps a change; null when none is within the blend distance or a point nobody changed
    /// lies nearer than every such point.
    /// </summary>
    /// <remarks>
    /// A point nobody changed ends at its earlier height like the restored point, so the cleaned mod's own edge lies behind it and a
    /// blend across it would leave a ridge on ground that was flat.
    /// </remarks>
    private float? DistanceToChangeKept(WorldGridPoint world, IReadOnlySet<WorldGridPoint> restored)
    {
        float? unchangedAt = null;
        foreach (var step in _lands.BlendSteps)
        {
            if (unchangedAt < step.Distance) return null;
            var point = world.StepBy(step);
            if (KeepsChange(point, restored)) return step.Distance;
            if (!restored.Contains(point)) unchangedAt ??= step.Distance;
        }
        return null;
    }

    /// <summary>
    /// Whether the point ends away from its height before the cleaned mod: it does not go back, and a record holding it changed its
    /// height. A point no record changed has no step to blend into.
    /// </summary>
    private bool KeepsChange(WorldGridPoint world, IReadOnlySet<WorldGridPoint> restored)
    {
        if (restored.Contains(world)) return false;
        foreach (var holder in world.Holders())
        {
            if (_lands.ByCell.TryGetValue(holder.Cell, out var land) && land.Change.Heights is { } heights
                && HeightGridComparison.DiffersAt(heights.Earlier, heights.Current, holder.Point, _lands.HeightTolerance))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The points the cleaned mod last changed that a cleared quarter holds and that do not go back: a candidate by its reason, any
    /// other point because another quarter holding it is not cleared or a version holds no height map.
    /// </summary>
    private ImmutableArray<LandPointsKept> KeptPoints(LandAreasJudged land, PointSet candidates, PointSet restored, Outcome outcome) =>
    [
        .. land.Change.UndoPoints.Points
            .Where(point => !restored.Contains(point) && TerrainQuadrants.Holding(point).Any(quarter => land.Quarters[quarter].Verdict == AreaVerdict.Cleared))
            .GroupBy(point => candidates.Contains(point) ? KeptReasonOf(new LandGridPoint(land.Change.Cell, point), outcome.TooSteep) : ReasonNoCandidateIsKept(land.Change))
            .OrderBy(group => group.Key)
            .Select(group => new LandPointsKept(group.Key, PointSet.Of(group))),
    ];

    /// <summary>Why a point in a cleared quarter is no candidate (<see cref="Lands.Candidates"/>).</summary>
    private static TerrainKeptReason ReasonNoCandidateIsKept(LandChangedByCleanedMod change) =>
        change.Heights is { Earlier: not null, Current: not null } ? TerrainKeptReason.NextToQuarterThatStays : TerrainKeptReason.WithoutHeightMap;

    /// <summary>
    /// The point's own protection first, then a protection of another record holding it, then a cell too steep to store, then a
    /// record holding it that does not let it go back, then the records' earlier heights; a point none of these keeps is kept by
    /// its records' steps, the only other reason a point that every record frees does not go back.
    /// </summary>
    private TerrainKeptReason KeptReasonOf(LandGridPoint point, IReadOnlySet<ExteriorCell> tooSteep)
    {
        var holders = WorldGridPoint.Of(point).Holders().OrderBy(holder => holder != point).ToList();
        foreach (var holder in holders)
        {
            if (_protected.TryGetValue(holder, out var reason)) return reason;
        }
        if (holders.Any(holder => tooSteep.Contains(holder.Cell))) return TerrainKeptReason.TooSteepToStore;
        if (!holders.All(IsFree)) return TerrainKeptReason.OnEdgeOfLandThatStays;
        return EarlierHeightsAgree(WorldGridPoint.Of(point).Holders()) ? TerrainKeptReason.EdgeStoredOnDifferentSteps : TerrainKeptReason.EarlierEdgeHeightsDiffer;
    }

    /// <summary>Whether every record holding the point lets it go back by the protection alone.</summary>
    private bool GoesBack(WorldGridPoint world)
    {
        var holders = world.Holders();
        foreach (var holder in holders)
        {
            if (!IsFree(holder)) return false;
        }
        return EarlierHeightsAgree(holders) && StepsAgree(holders);
    }

    private bool IsFree(LandGridPoint point) =>
        _lands.Candidates.TryGetValue(point.Cell, out var candidates) && candidates.Contains(point.Point) && !_protected.ContainsKey(point);

    private bool EarlierHeightsAgree(LandGridPointHolders holders)
    {
        var first = EarlierHeightAt(holders[0]);
        foreach (var holder in holders)
        {
            if (MathF.Abs(EarlierHeightAt(holder) - first) > _lands.HeightTolerance) return false;
        }
        return true;
    }

    /// <summary>Whether the records' winning heights lie whole steps apart, so a height on one record's steps lies on every one's.</summary>
    private bool StepsAgree(LandGridPointHolders holders)
    {
        var first = _lands.HeightsOf(holders[0].Cell).Current![0];
        foreach (var holder in holders)
        {
            var steps = (_lands.HeightsOf(holder.Cell).Current![0] - first) / UnitsPerStep;
            if (MathF.Abs(steps - MathF.Floor(steps + 0.5f)) > SameStepsTolerance) return false;
        }
        return true;
    }

    private float EarlierHeightAt(LandGridPoint point) => _lands.HeightsOf(point.Cell).Earlier![point.Point];

    /// <summary>The final heights of the neighbouring cells next to the cell's edges; a side is unknown where the plan holds no heights of that cell.</summary>
    private CellEdgeHeights BeyondOf(ExteriorCell cell, Outcome outcome) => CellEdgeHeights.Beside(
        FinalHeightsOf(cell with { X = cell.X - 1 }, outcome),
        FinalHeightsOf(cell with { X = cell.X + 1 }, outcome),
        FinalHeightsOf(cell with { Y = cell.Y - 1 }, outcome),
        FinalHeightsOf(cell with { Y = cell.Y + 1 }, outcome));

    private float[]? FinalHeightsOf(ExteriorCell cell, Outcome outcome) => outcome.Final.GetValueOrDefault(cell) ?? CurrentHeightsOf(cell);

    /// <summary>What every plan protecting points of the same LAND records shares.</summary>
    private sealed class Lands
    {
        public Lands(IReadOnlyList<LandAreasJudged> lands, Func<ExteriorCell, LandHeights> readHeights, float heightTolerance, int blendDistance)
        {
            All = [.. lands];
            ByCell = lands.ToDictionary(land => land.Change.Cell);
            Candidates = lands.ToDictionary(land => land.Change.Cell, land => CandidatesOf(land.Change, land.Quarters));
            ColourCandidates = lands.ToDictionary(land => land.Change.Cell, land => ColourCandidatesOf(land.Change, land.Quarters));
            ReadHeights = readHeights;
            HeightTolerance = heightTolerance;
            BlendDistance = blendDistance;
            BlendSteps = GridStep.Within(blendDistance);
        }

        public ImmutableArray<LandAreasJudged> All { get; }

        public IReadOnlyDictionary<ExteriorCell, LandAreasJudged> ByCell { get; }

        /// <summary>Per cell, the points whose height goes back by their own record alone: undone, earlier and current heights known, every quarter holding them cleared.</summary>
        public IReadOnlyDictionary<ExteriorCell, PointSet> Candidates { get; }

        /// <summary>Per cell, the points whose colour goes back by their own record alone: undone and every quarter holding them cleared.</summary>
        public IReadOnlyDictionary<ExteriorCell, PointSet> ColourCandidates { get; }

        public Func<ExteriorCell, LandHeights> ReadHeights { get; }

        public float HeightTolerance { get; }

        /// <summary>In grid steps.</summary>
        public int BlendDistance { get; }

        /// <summary>The grid steps to the points within the blend distance, nearest first.</summary>
        public ImmutableArray<GridStep> BlendSteps { get; }

        public LandHeights HeightsOf(ExteriorCell cell) => HeightsOf(ByCell[cell].Change);

        public LandHeights HeightsOf(LandChangedByCleanedMod land) => land.Heights ?? ReadHeights(land.Cell);

        /// <param name="quarters">Empty for a LAND record that never goes back, which then has none.</param>
        private static PointSet CandidatesOf(LandChangedByCleanedMod change, ImmutableArray<LandQuarterJudged> quarters)
        {
            if (quarters.IsEmpty || change.Heights is not { Earlier: not null, Current: not null }) return PointSet.Of([]);
            return PointSet.Of(change.UndoPoints.Points.Where(point => InClearedQuarters(quarters, point)));
        }

        /// <param name="quarters">Empty for a LAND record that never goes back, which then has none.</param>
        private static PointSet ColourCandidatesOf(LandChangedByCleanedMod change, ImmutableArray<LandQuarterJudged> quarters) =>
            quarters.IsEmpty ? PointSet.Of([]) : PointSet.Of(change.UndoColours.Points.Where(point => InClearedQuarters(quarters, point)));

        /// <summary>Whether every quarter holding the point is cleared; a point on a line between quarters lies in two or four of them.</summary>
        private static bool InClearedQuarters(ImmutableArray<LandQuarterJudged> quarters, int point) =>
            TerrainQuadrants.Holding(point).All(quarter => quarters[quarter].Verdict == AreaVerdict.Cleared);
    }

    /// <param name="Changed">The cells whose final heights were set or removed last.</param>
    private sealed record Work(HashSet<WorldGridPoint> Restored, Dictionary<ExteriorCell, float[]> Final, HashSet<ExteriorCell> Changed);

    /// <param name="Restored">The world points whose height goes back.</param>
    /// <param name="Final">Per cell with a restored point, every point's height.</param>
    /// <param name="TooSteep">The cells found to keep every height, since their heights with any restored could not be stored.</param>
    private sealed record Outcome(IReadOnlySet<WorldGridPoint> Restored, IReadOnlyDictionary<ExteriorCell, float[]> Final, IReadOnlySet<ExteriorCell> TooSteep);
}
