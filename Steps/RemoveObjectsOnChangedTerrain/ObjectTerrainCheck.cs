using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>The outcome of checking one target object.</summary>
/// <param name="WithoutTerrain">The object would be checked but has no terrain to compare.</param>
/// <param name="Failure">Why the check stopped unexpectedly; the object is then not a hit.</param>
internal readonly record struct ObjectCheck(ObjectOnChangedTerrain? Hit, bool WithoutTerrain, string? Failure)
{
    public static ObjectCheck NothingFound => default;

    public static ObjectCheck NoTerrain => new(Hit: null, WithoutTerrain: true, Failure: null);
}

/// <summary>
/// Checks whether another mod changed the terrain around a target object. Each cell's versions are compared once per step call
/// and each object's check depends only on that comparison, so the result does not depend on the thread count. Thread-safe.
/// </summary>
internal sealed class ObjectTerrainCheck(IPluginRecords records, IBaseObjectShapes shapes, TerrainPlugins plugins, ChangedTerrainSettings settings)
{
    private readonly ComputedOncePerKey<ExteriorCell, CellTerrain?> _cells = new(Publication.BuiltOnce, EqualityComparer<ExteriorCell>.Default);
    private readonly CheckArea _area = new(shapes, settings);
    private readonly CellTerrainComparison _comparison = new(plugins, new TerrainTypes(records), settings);

    /// <summary>The cells read so far that have a reference terrain.</summary>
    public int CellsCompared() => _cells.Contents().Count(cell => cell != null);

    /// <remarks>An object whose check fails is not removed; the failure is returned with the object.</remarks>
    public ObjectCheck Check(TargetObject target)
    {
        try
        {
            return IsCovered(target) ? CompareTerrain(target) : ObjectCheck.NothingFound;
        }
        catch (Exception ex) when (Failures.IsRecoverable(ex))
        {
            return new ObjectCheck(Hit: null, WithoutTerrain: false, Failures.DescribeFailure(ex));
        }
    }

    private bool IsCovered(TargetObject target) =>
        settings.Objects == TerrainCheckedObjects.AllObjects || shapes.VisibilityOf(target).IsVisible;

    private ObjectCheck CompareTerrain(TargetObject target)
    {
        if (records.FindLandWorldspace(target.SpaceKey) is not { } landWorldspace) return ObjectCheck.NoTerrain;
        var area = _area.Around(target);
        var cells = CellsUnder(landWorldspace, area);
        if (cells.Count == 0) return ObjectCheck.NoTerrain;
        if (settings.Objects == TerrainCheckedObjects.VisibleObjectsOnTheGround && !ReachesTheGround(area, cells)) return ObjectCheck.NothingFound;

        return FindChange(target, cells) is { } hit ? new ObjectCheck(hit, WithoutTerrain: false, Failure: null) : ObjectCheck.NothingFound;
    }

    /// <summary>The cells under the area that have a reference terrain; a cell without one is not checked.</summary>
    private List<CheckedCell> CellsUnder(RecordKey landWorldspace, Box area)
    {
        var cells = new List<CheckedCell>();
        foreach (var under in _area.VerticesUnder(area))
        {
            if (TerrainOf(new ExteriorCell(landWorldspace, under.CellX, under.CellY)) is { } terrain) cells.Add(new CheckedCell(terrain, under.VertexIndices));
        }
        return cells;
    }

    private CellTerrain? TerrainOf(ExteriorCell cell) => _cells.Get(cell, () => _comparison.Compare(records.ReadTerrainVersions(cell)));

    /// <summary>Whether the area's height range overlaps the heights at its vertices, as the terrain was (the reference) or as it is now (the winner).</summary>
    private bool ReachesTheGround(Box area, List<CheckedCell> cells) =>
        OverlapsHeights(area, cells, static terrain => terrain.ReferenceHeights) || OverlapsHeights(area, cells, static terrain => terrain.WinnerHeights);

    /// <remarks>A version without heights adds no heights.</remarks>
    private bool OverlapsHeights(Box area, List<CheckedCell> cells, Func<CellTerrain, float[]?> heightsOf)
    {
        var (any, lowest, highest) = (false, float.MaxValue, float.MinValue);
        foreach (var (terrain, vertices) in cells)
        {
            if (heightsOf(terrain) is not { } heights) continue;
            foreach (var vertex in vertices) (any, lowest, highest) = (true, MathF.Min(lowest, heights[vertex]), MathF.Max(highest, heights[vertex]));
        }
        return any && lowest <= area.Max.Z && highest >= area.Min.Z;
    }

    /// <summary>
    /// How the winner differs at the area's vertices that a counting plugin changed (the change the game shows), and the last
    /// loading counting plugin that changed one of them.
    /// </summary>
    /// <returns>Null when no counting plugin changed a vertex of the area.</returns>
    private ObjectOnChangedTerrain? FindChange(TargetObject target, List<CheckedCell> cells)
    {
        var shown = default(TerrainChange);
        int? lastChanger = null;
        foreach (var (terrain, vertices) in cells)
        {
            if (terrain.Changes is not { } changes) continue;
            foreach (var vertex in vertices)
            {
                var change = changes[vertex];
                if (!change.Shown.Any) continue;
                shown = shown.With(change.Shown);
                lastChanger = Math.Max(lastChanger ?? change.LastChanger, change.LastChanger);
            }
        }
        return lastChanger is { } position ? new ObjectOnChangedTerrain(target.Id, plugins.PluginAt(position), shown.Kind) : null;
    }
}
