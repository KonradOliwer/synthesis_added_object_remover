using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <remarks>
/// Everything runs in ascending order on one thread, so the plan does not depend on the thread count. A plan protecting more points
/// than one already computed starts from that one's outcome and recomputes only the cells near the points it takes back; the
/// outcome is the same as computing it afresh.
/// </remarks>
internal sealed partial class TerrainUndoPlan
{
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
        foreach (var land in _lands.Planned)
        {
            foreach (var point in _lands.Candidates[land.Change.Cell].Points)
            {
                var world = WorldGridPoint.Of(new LandGridPoint(land.Change.Cell, point));
                if (_points.GoesBack(world)) restored.Add(world);
            }
        }
        var final = new Dictionary<ExteriorCell, float[]>();
        var cells = _lands.Planned.Select(land => land.Change.Cell).ToHashSet();
        foreach (var cell in cells) _heights.UpdateFinalHeights(cell, restored, final);
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
        var changed = _heights.CellsNear(takenBack);
        foreach (var cell in changed) _heights.UpdateFinalHeights(cell, restored, final);
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
            changed = _heights.CellsNear(takenBack);
            foreach (var cell in changed) _heights.UpdateFinalHeights(cell, work.Restored, work.Final);
        }
    }

    /// <param name="Changed">The cells whose final heights were set or removed last.</param>
    private sealed record Work(HashSet<WorldGridPoint> Restored, Dictionary<ExteriorCell, float[]> Final, HashSet<ExteriorCell> Changed);

    /// <param name="Restored">The world points whose height goes back.</param>
    /// <param name="Final">Per cell with a restored point, every point's height.</param>
    /// <param name="TooSteep">The cells found to keep every height, since their heights with any restored could not be stored.</param>
    private sealed record Outcome(IReadOnlySet<WorldGridPoint> Restored, IReadOnlyDictionary<ExteriorCell, float[]> Final, IReadOnlySet<ExteriorCell> TooSteep);
}
