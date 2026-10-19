using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
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
/// <remarks>
/// The land and the water of a cell are compared apart: a child worldspace can use its parent's land, which is then compared once
/// for both, while the water fields belong to each worldspace's own cell.
/// </remarks>
internal sealed class ObjectTerrainCheck(IPluginRecords records, IBaseObjectShapes shapes, TerrainPlugins plugins, ChangedTerrainSettings settings)
{
    /// <summary>By land cell: the cell in the worldspace whose land it uses.</summary>
    private readonly ComputedOncePerKey<ExteriorCell, CellTerrain?> _landCells = new(Publication.BuiltOnce, EqualityComparer<ExteriorCell>.Default);

    /// <summary>By the cell in the object's own worldspace.</summary>
    private readonly ComputedOncePerKey<ExteriorCell, CellWaterChange?> _waterCells = new(Publication.BuiltOnce, EqualityComparer<ExteriorCell>.Default);

    private readonly CheckArea _area = new(shapes, settings);
    private readonly CellTerrainComparison _terrain = new(plugins, new TerrainTypes(records), settings);

    /// <summary>Null when no water change is counted; the water is then not read.</summary>
    private readonly CellWaterComparison? _water = settings.CountsWaterChanges ? new CellWaterComparison(plugins, settings) : null;

    /// <summary>The land cells read so far that have a reference terrain.</summary>
    public int CellsCompared() => _landCells.Contents().Count(cell => cell != null);

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
        settings.Objects == TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes || shapes.VisibilityOf(target).IsVisible;

    /// <remarks>The first area that holds a change gives the finding, as the first removal zone that finds an object does in the too-close step.</remarks>
    private ObjectCheck CompareTerrain(TargetObject target)
    {
        if (records.FindLandWorldspace(target.SpaceKey) is not { } landWorldspace) return ObjectCheck.NoTerrain;
        var anyAreaHasTerrain = false;
        foreach (var area in _area.Around(target))
        {
            var cells = CellsUnder(target.SpaceKey, landWorldspace, area);
            if (cells.Count == 0) continue;
            anyAreaHasTerrain = true;
            if (settings.Objects == TerrainCheckedObjects.VisibleObjectsOnTheGround && !ReachesTheGround(area, cells)) continue;
            if (FindChange(target, cells) is { } hit) return new ObjectCheck(hit, WithoutTerrain: false, Failure: null);
        }
        return anyAreaHasTerrain ? ObjectCheck.NothingFound : ObjectCheck.NoTerrain;
    }

    /// <summary>The cells under the area that have a reference terrain; a cell without one is not checked.</summary>
    private List<CheckedCell> CellsUnder(RecordKey space, RecordKey landWorldspace, Box area)
    {
        var cells = new List<CheckedCell>();
        foreach (var under in _area.VerticesUnder(area))
        {
            var cell = new ExteriorCell(space, under.CellX, under.CellY);
            if (CompareLand(cell with { WorldspaceKey = landWorldspace }) is { } terrain) cells.Add(new CheckedCell(terrain, CompareWater(cell), under.VertexIndices));
        }
        return cells;
    }

    private CellTerrain? CompareLand(ExteriorCell landCell) => _landCells.Get(landCell, () => _terrain.Compare(records.ReadTerrainVersions(landCell)));

    /// <returns>Null when no water change is counted.</returns>
    private CellWaterChange? CompareWater(ExteriorCell cell) =>
        _water is { } water ? _waterCells.Get(cell, () => water.Compare(records.ReadWaterCopies(cell))) : null;

    /// <summary>Whether the area's height range overlaps the heights at its vertices, as the terrain was (the reference) or as it is now (the winner).</summary>
    private bool ReachesTheGround(Box area, List<CheckedCell> cells) =>
        OverlapsHeights(area, cells, static terrain => terrain.ReferenceHeights) || OverlapsHeights(area, cells, static terrain => terrain.WinnerHeights);

    /// <remarks>A version without heights adds no heights.</remarks>
    private bool OverlapsHeights(Box area, List<CheckedCell> cells, Func<CellTerrain, float[]?> heightsOf)
    {
        var (any, lowest, highest) = (false, float.MaxValue, float.MinValue);
        foreach (var cell in cells)
        {
            if (heightsOf(cell.Terrain) is not { } heights) continue;
            foreach (var vertex in cell.Vertices)(any, lowest, highest) = (true, MathF.Min(lowest, heights[vertex]), MathF.Max(highest, heights[vertex]));
        }
        return any && lowest <= area.Max.Z && highest >= area.Min.Z;
    }

    /// <summary>
    /// The counted changes of the area: a height change at any vertex, a terrain type change at enough of its vertices, and a
    /// water change that covered or uncovered enough of them. The kind names the counted changes; the plugin is the later loading
    /// of the plugins each counted change names.
    /// </summary>
    /// <returns>Null when no change of the area counts.</returns>
    private ObjectOnChangedTerrain? FindChange(TargetObject target, List<CheckedCell> cells)
    {
        var changes = AreaChanges.Count(cells);
        var heightChanger = changes.HeightChanger;
        var typeChanger = changes.TypeChanger(settings.MinTerrainTypeChangePercent);
        var waterChanger = changes.WaterChanger(settings.MinWaterCoverChangePercent);
        if (new[] { heightChanger, typeChanger, waterChanger }.Max() is not { } changer) return null;
        var kind = (heightChanger != null ? TerrainChangeKind.Height : TerrainChangeKind.None)
            | (typeChanger != null ? TerrainChangeKind.TerrainType : TerrainChangeKind.None)
            | (waterChanger != null ? changes.WaterKinds : TerrainChangeKind.None);
        return new ObjectOnChangedTerrain(target.Id, plugins.PluginAt(changer), kind);
    }
}
