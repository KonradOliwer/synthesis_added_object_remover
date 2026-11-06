using System.Collections.Immutable;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>The counted changes at the vertices of one check area, and which plugin each removal by height, terrain type or water names.</summary>
/// <param name="Vertices">
/// The checked vertices of the area in the cells under it, counted once per cell: the total the water share is measured against.
/// </param>
/// <param name="TypeVertices">
/// Of <paramref name="Vertices"/>, those with an accepted terrain type: the total the terrain type share is measured against.
/// </param>
/// <param name="HeightChanger">
/// The last loading plugin that changed the height of a vertex, the patch only when it alone did; null when none did.
/// </param>
/// <param name="TypeChangesByChanger">Per plugin that changed the terrain type of vertices, how many it changed.</param>
/// <param name="WaterFlipsByChanger">Per plugin whose water change covered or uncovered vertices, how many.</param>
/// <param name="WaterKinds">The kinds of the water changes that covered or uncovered a vertex.</param>
internal sealed record AreaChanges(
    int Vertices,
    int TypeVertices,
    int? HeightChanger,
    IReadOnlyDictionary<int, int> TypeChangesByChanger,
    IReadOnlyDictionary<int, int> WaterFlipsByChanger,
    TerrainChangeKind WaterKinds)
{
    /// <remarks>
    /// A vertex without an accepted height or terrain type is not checked: it counts for no change and in no total. A vertex without
    /// an accepted terrain type is left out of the terrain type total only. The changes <paramref name="reach"/> leaves out count for
    /// nothing, while their vertices stay in the totals.
    /// </remarks>
    public static AreaChanges Count(List<CheckedCell> cells, AreaReach reach)
    {
        var (vertexCount, typeVertexCount, heightChanger, typeChanges) = (0, 0, (int?)null, new Dictionary<int, int>());
        var (waterFlips, waterKinds) = (new Dictionary<int, int>(), TerrainChangeKind.None);
        foreach (var cell in cells)
        {
            var waterCounts = reach.CountsWaterOf(cell);
            foreach (var vertex in cell.Vertices.Where(cell.Terrain.IsCheckedAt))
            {
                vertexCount++;
                if (cell.Terrain.IsTypeCheckedAt(vertex)) typeVertexCount++;
                if (waterCounts && cell.WaterFlipAt(vertex) is { } water)
                {
                    waterFlips[water.Changer] = waterFlips.GetValueOrDefault(water.Changer) + 1;
                    waterKinds |= water.Kind;
                }
                if (!reach.Land || cell.Terrain.Changes?[vertex] is not { } change) continue;
                if (change.HeightChanger is { } height) heightChanger = Math.Max(heightChanger ?? height, height);
                if (change.TypeChanger is { } type) typeChanges[type] = typeChanges.GetValueOrDefault(type) + 1;
            }
        }
        return new AreaChanges(vertexCount, typeVertexCount, heightChanger, typeChanges, waterFlips, waterKinds);
    }

    /// <summary>
    /// The evidence of the counted changes: per cell, the checked vertices whose height or terrain type changed, where that kind of
    /// change counts, and the checked vertices a water change that counts covered or uncovered. A cell without any is left out.
    /// </summary>
    public static ImmutableArray<CountedCell> CountedCells(List<CheckedCell> cells, AreaReach reach, bool heightsCount, bool typesCount, bool waterCounts) =>
    [
        .. cells
            .Select(cell => CountedCellOf(cell, heightsCount, typesCount, waterCounts && reach.CountsWaterOf(cell) ? WaterFlippedVertices(cell) : []))
            .Where(counted => !counted.HeightVertices.IsEmpty || !counted.TypeVertices.IsEmpty || !counted.WaterFlipped.IsEmpty),
    ];

    private static CountedCell CountedCellOf(CheckedCell cell, bool heightsCount, bool typesCount, ImmutableArray<int> waterFlipped) =>
        new(
            cell.LandCell,
            cell.WaterCell,
            heightsCount ? ChangedVertices(cell, static change => change.HeightChanger != null) : [],
            typesCount ? ChangedVertices(cell, static change => change.TypeChanger != null) : [],
            waterFlipped);

    private static ImmutableArray<int> ChangedVertices(CheckedCell cell, Func<VertexChange, bool> changed) =>
        [.. cell.Vertices.Where(vertex => cell.Terrain.IsCheckedAt(vertex) && cell.Terrain.Changes?[vertex] is { } change && changed(change)).Order()];

    private static ImmutableArray<int> WaterFlippedVertices(CheckedCell cell) =>
        [.. cell.Vertices.Where(vertex => cell.Terrain.IsCheckedAt(vertex) && cell.WaterFlipAt(vertex) != null).Order()];

    /// <summary>
    /// The terrain type change counts when at least one vertex changed and the changed vertices make up at least
    /// <paramref name="minimumPercent"/> percent of the area's vertices that have an accepted terrain type.
    /// </summary>
    /// <returns>
    /// Of the real mods that changed the type of vertices, the one that changed the most, the later loading one on a tie, and the
    /// patch only when it alone changed them; null when the change does not count.
    /// </returns>
    public int? TypeChanger(int minimumPercent) => FindMainChangerOfShare(TypeChangesByChanger, TypeVertices, minimumPercent);

    /// <summary>
    /// The water change counts when at least one vertex was covered or uncovered and those vertices make up at least
    /// <paramref name="minimumPercent"/> percent of the area's checked vertices.
    /// </summary>
    /// <returns>
    /// Of the real mods whose water change covered or uncovered vertices, the one that did so at the most, the later loading one on a
    /// tie, and the patch only when it alone did; null when the change does not count.
    /// </returns>
    public int? WaterChanger(int minimumPercent) => FindMainChangerOfShare(WaterFlipsByChanger, Vertices, minimumPercent);

    private int? FindMainChangerOfShare(IReadOnlyDictionary<int, int> changesByChanger, int total, int minimumPercent) =>
        ReachesShare(changesByChanger.Values.Sum(), total, minimumPercent)
            ? changesByChanger.MaxBy(changer => (changer.Key != TerrainPlugins.PatchOnlyChange, changer.Value, changer.Key)).Key
            : null;

    private bool ReachesShare(int changed, int total, int minimumPercent) => changed > 0 && SharePercent.AtLeast(changed, total, minimumPercent);
}
