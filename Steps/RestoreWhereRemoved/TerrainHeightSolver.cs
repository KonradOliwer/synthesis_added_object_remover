using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The heights a LAND record gets once written. Restored points near a point that keeps a change are blended into it, and every
/// planned height lies on the steps its record stores, so ground that stays keeps its exact height.
/// </summary>
internal sealed class TerrainHeightSolver(TerrainUndoLands lands)
{
    private readonly TerrainUndoLands _lands = lands;

    /// <summary>The cells holding a point within the blend distance of one of the points, whose final heights depend on them.</summary>
    public HashSet<ExteriorCell> CellsNear(List<WorldGridPoint> points)
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
    public void UpdateFinalHeights(ExteriorCell cell, IReadOnlySet<WorldGridPoint> restored, Dictionary<ExteriorCell, float[]> final)
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
}
