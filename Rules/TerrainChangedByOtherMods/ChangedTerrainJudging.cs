using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>The rule over any view of the terrain, which trusts the plugins as <paramref name="plugins"/> does.</summary>
/// <param name="loadOrder">The terrain and water of the load order as read; it also tells the land each space uses.</param>
internal sealed class ChangedTerrainJudging(TerrainPlugins plugins, ITerrainView loadOrder, IBaseObjectShapes shapes) : IChangedTerrainJudging
{
    public ITerrainView LoadOrder => loadOrder;

    public IObjectTerrainChecks Over(ITerrainView view) => new ObjectTerrainChecks(view, shapes, plugins);

    public ImmutableArray<int> FindObjectsReading(IReadOnlyList<(TargetObject Object, ChangedTerrainSettings Settings)> objects, IReadOnlySet<ExteriorCell> cells) =>
        [.. Enumerable.Range(0, objects.Count).Where(index => FindCellsRead(objects[index].Object, objects[index].Settings).Overlaps(cells))];

    public IReadOnlySet<ExteriorCell> FindCellsRead(TargetObject target, ChangedTerrainSettings settings)
    {
        var area = new CheckArea(shapes, settings);
        if (!settings.Runs || !area.Covers(target) || loadOrder.FindLandWorldspace(target.SpaceKey) is not { } landWorldspace) return new HashSet<ExteriorCell>();
        return area.Around(target)
            .SelectMany(around => area.VerticesUnder(around))
            .Select(under => new ExteriorCell(target.SpaceKey, under.CellX, under.CellY))
            .SelectMany(cell => new[] { cell, cell with { WorldspaceKey = landWorldspace } })
            .ToHashSet();
    }
}
