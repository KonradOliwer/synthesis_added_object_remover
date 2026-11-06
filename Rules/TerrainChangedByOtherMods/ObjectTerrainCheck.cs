using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>The outcome of checking one target object.</summary>
/// <param name="WithoutTerrain">The object would be checked but has no terrain to compare.</param>
/// <param name="Failure">Why the check stopped unexpectedly; the object is then not a hit.</param>
internal readonly record struct ObjectCheck(ObjectOnChangedTerrain? Hit, bool WithoutTerrain, string? Failure)
{
    public static ObjectCheck NothingFound => default;

    public static ObjectCheck NoTerrain => new(Hit: null, WithoutTerrain: true, Failure: null);
}

/// <summary>
/// Checks whether another mod changed the terrain around a target object. Each cell's versions are compared once per check
/// and each object's check depends only on that comparison, so the result does not depend on the thread count. Thread-safe.
/// </summary>
/// <remarks>
/// The land and the water of a cell are compared apart: a child worldspace can use its parent's land, which is then compared once
/// for both, while the water fields belong to each worldspace's own cell.
/// </remarks>
internal sealed class ObjectTerrainCheck(ITerrainView view, IBaseObjectShapes shapes, TerrainPlugins plugins, ChangedTerrainSettings settings)
{
    /// <summary>By land cell: the cell in the worldspace whose land it uses.</summary>
    private readonly ComputedOncePerKey<ExteriorCell, CellTerrain?> _landCells = new(Publication.BuiltOnce, EqualityComparer<ExteriorCell>.Default);

    /// <summary>By the cell in the object's own worldspace.</summary>
    private readonly ComputedOncePerKey<ExteriorCell, WaterComparison> _waterCells = new(Publication.BuiltOnce, EqualityComparer<ExteriorCell>.Default);

    /// <summary>The cells, in the object's own worldspace, without checked land whose water was compared over the shown ground.</summary>
    private readonly ComputedOncePerKey<ExteriorCell, bool> _waterOnlyCells = new(Publication.BuiltOnce, EqualityComparer<ExteriorCell>.Default);

    private readonly CheckArea _area = new(shapes, settings);
    private readonly CellTerrainComparison _terrain = new(plugins, new TerrainTypes(view), settings);

    /// <summary>Null when no water change is counted; the water is then not read.</summary>
    private readonly CellWaterComparison? _water = settings.CountsWaterChanges ? new CellWaterComparison(plugins, settings) : null;

    /// <summary>The land cells read so far that have an accepted terrain.</summary>
    public int CellsCompared() => _landCells.Contents().Count(cell => cell is { ShownOnly: false });

    /// <summary>The cells read so far without checked land whose water was compared.</summary>
    public int CellsComparedForWaterOnly() => _waterOnlyCells.Contents().Count;

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

    private bool IsCovered(TargetObject target) => _area.Covers(target);

    /// <remarks>The first area that holds a change gives the finding, as the first removal zone that finds an object does in the too-close rule.</remarks>
    private ObjectCheck CompareTerrain(TargetObject target)
    {
        if (view.FindLandWorldspace(target.SpaceKey) is not { } landWorldspace) return ObjectCheck.NoTerrain;
        var anyAreaHasTerrain = false;
        foreach (var area in _area.Around(target))
        {
            var cells = CellsUnder(target.SpaceKey, landWorldspace, area);
            if (cells.Count == 0) continue;
            anyAreaHasTerrain = true;
            var reach = ReachOf(area, cells);
            if (!reach.Any) continue;
            if (FindChange(target, cells, reach) is { } hit) return new ObjectCheck(hit, WithoutTerrain: false, Failure: null);
        }
        return anyAreaHasTerrain ? ObjectCheck.NothingFound : ObjectCheck.NoTerrain;
    }

    /// <summary>
    /// The cells under the area that have checked land, and those without it whose water is checked, compared over the ground as the
    /// game shows it; a cell without checked land is included only when its water is checked.
    /// </summary>
    private List<CheckedCell> CellsUnder(RecordKey space, RecordKey landWorldspace, Box area)
    {
        var cells = new List<CheckedCell>();
        foreach (var under in _area.VerticesUnder(area))
        {
            var cell = new ExteriorCell(space, under.CellX, under.CellY);
            var landCell = cell with { WorldspaceKey = landWorldspace };
            if (CompareLand(landCell) is not { } terrain) continue;
            var water = CompareWater(cell);
            if (terrain.ShownOnly && !water.IsChecked) continue;
            if (terrain.ShownOnly) _waterOnlyCells.Get(cell, static () => true);
            cells.Add(new CheckedCell(landCell, cell, terrain, water.Change, under.VertexIndices));
        }
        return cells;
    }

    /// <summary>Whether the object reaches the ground of the cells with checked land, else which of the cells without it it reaches.</summary>
    private AreaReach ReachOf(Box area, List<CheckedCell> cells)
    {
        if (ReachesGround(area, cells.Where(static cell => !cell.Terrain.ShownOnly))) return new AreaReach(Land: true, []);
        return new AreaReach(Land: false, [.. cells.Where(cell => cell.Terrain.ShownOnly && ReachesGround(area, [cell])).Select(static cell => cell.WaterCell)]);
    }

    private bool ReachesGround(Box area, IEnumerable<CheckedCell> cells)
    {
        List<CheckedCell> reached = [.. cells];
        return _area.ChecksArea(area, HeightsAt(reached, static terrain => terrain.AcceptedHeights), HeightsAt(reached, static terrain => terrain.WinnerHeights));
    }

    private CellTerrain? CompareLand(ExteriorCell landCell) => _landCells.Get(landCell, () => _terrain.Compare(view.ReadTerrainVersions(landCell), view.ReadRestoredParts(landCell)));

    /// <returns>Not checked when no water change is counted.</returns>
    private WaterComparison CompareWater(ExteriorCell cell) =>
        _water is { } water ? _waterCells.Get(cell, () => water.Compare(view.ReadWaterCopies(cell), view.ReadRestoredParts(cell))) : WaterComparison.NotChecked;

    /// <summary>
    /// The heights at the cells' checked vertices in one version of the ground: as accepted or as the game shows them (the winner).
    /// </summary>
    /// <remarks>A version without heights adds no heights.</remarks>
    private static IEnumerable<float> HeightsAt(List<CheckedCell> cells, Func<CellTerrain, float[]?> heightsOf) =>
        cells.SelectMany(cell => heightsOf(cell.Terrain) is { } heights ? cell.Vertices.Where(cell.Terrain.IsCheckedAt).Select(vertex => heights[vertex]) : []);

    /// <summary>
    /// The counted changes of the area: a height change at any vertex, a terrain type change at enough of its vertices, and a
    /// water change that covered or uncovered enough of them. The kind names the counted changes; the plugin is the later loading
    /// of the real mods the counted changes name, and none when only the patch holds them (an earlier patcher of the group made them).
    /// </summary>
    /// <returns>Null when no change of the area counts.</returns>
    private ObjectOnChangedTerrain? FindChange(TargetObject target, List<CheckedCell> cells, AreaReach reach)
    {
        var changes = AreaChanges.Count(cells, reach);
        var heightChanger = changes.HeightChanger;
        var typeChanger = changes.TypeChanger(settings.MinTerrainTypeChangePercent);
        var waterChanger = changes.WaterChanger(settings.MinWaterCoverChangePercent);
        if (new[] { heightChanger, typeChanger, waterChanger }.Max() is not { } changer) return null;
        var kind = (heightChanger != null ? TerrainChangeKind.Height : TerrainChangeKind.None)
            | (typeChanger != null ? TerrainChangeKind.TerrainType : TerrainChangeKind.None)
            | (waterChanger != null ? changes.WaterKinds : TerrainChangeKind.None);
        var evidence = AreaChanges.CountedCells(cells, reach, heightChanger != null, typeChanger != null, waterChanger != null);
        return new ObjectOnChangedTerrain(target.Id, plugins.ChangerAt(changer), kind, evidence);
    }
}
