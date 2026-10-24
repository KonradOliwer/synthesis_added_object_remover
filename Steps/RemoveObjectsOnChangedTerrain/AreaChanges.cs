using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>The counted changes at the vertices of one check area, and which plugin each removal by height, terrain type or water names.</summary>
/// <param name="Vertices">
/// The checked vertices of the area in the cells that have an accepted terrain, counted once per cell: the total the terrain type
/// and water shares are measured against.
/// </param>
/// <param name="HeightChanger">
/// The last loading plugin that changed the height of a vertex, the patch only when it alone did; null when none did.
/// </param>
/// <param name="TypeChangesByChanger">Per plugin that changed the terrain type of vertices, how many it changed.</param>
/// <param name="WaterFlipsByChanger">Per plugin whose water change covered or uncovered vertices, how many.</param>
/// <param name="WaterKinds">The kinds of the water changes that covered or uncovered a vertex.</param>
internal sealed record AreaChanges(
    int Vertices,
    int? HeightChanger,
    IReadOnlyDictionary<int, int> TypeChangesByChanger,
    IReadOnlyDictionary<int, int> WaterFlipsByChanger,
    TerrainChangeKind WaterKinds)
{
    /// <remarks>A vertex without an accepted height or terrain type is not checked: it counts for no change and not in the total.</remarks>
    public static AreaChanges Count(List<CheckedCell> cells)
    {
        var (vertexCount, heightChanger, typeChanges) = (0, (int?)null, new Dictionary<int, int>());
        var (waterFlips, waterKinds) = (new Dictionary<int, int>(), TerrainChangeKind.None);
        foreach (var cell in cells)
        {
            foreach (var vertex in cell.Vertices.Where(cell.Terrain.IsCheckedAt))
            {
                vertexCount++;
                if (cell.WaterFlipAt(vertex) is { } water)
                {
                    waterFlips[water.Changer] = waterFlips.GetValueOrDefault(water.Changer) + 1;
                    waterKinds |= water.Kind;
                }
                if (cell.Terrain.Changes?[vertex] is not { } change) continue;
                if (change.HeightChanger is { } height) heightChanger = Math.Max(heightChanger ?? height, height);
                if (change.TypeChanger is { } type) typeChanges[type] = typeChanges.GetValueOrDefault(type) + 1;
            }
        }
        return new AreaChanges(vertexCount, heightChanger, typeChanges, waterFlips, waterKinds);
    }

    /// <summary>
    /// The terrain type change counts when at least one vertex changed and the changed vertices make up at least
    /// <paramref name="minimumPercent"/> percent of the area's vertices.
    /// </summary>
    /// <returns>
    /// Of the real mods that changed the type of vertices, the one that changed the most, the later loading one on a tie, and the
    /// patch only when it alone changed them; null when the change does not count.
    /// </returns>
    public int? TypeChanger(int minimumPercent) => FindMainChangerOfShare(TypeChangesByChanger, minimumPercent);

    /// <summary>
    /// The water change counts when at least one vertex was covered or uncovered and those vertices make up at least
    /// <paramref name="minimumPercent"/> percent of the area's vertices.
    /// </summary>
    /// <returns>
    /// Of the real mods whose water change covered or uncovered vertices, the one that did so at the most, the later loading one on a
    /// tie, and the patch only when it alone did; null when the change does not count.
    /// </returns>
    public int? WaterChanger(int minimumPercent) => FindMainChangerOfShare(WaterFlipsByChanger, minimumPercent);

    private int? FindMainChangerOfShare(IReadOnlyDictionary<int, int> changesByChanger, int minimumPercent) =>
        ReachesShare(changesByChanger.Values.Sum(), minimumPercent)
            ? changesByChanger.MaxBy(changer => (changer.Key != TerrainPlugins.PatchOnlyChange, changer.Value, changer.Key)).Key
            : null;

    private bool ReachesShare(int changed, int minimumPercent) => changed > 0 && SharePercent.AtLeast(changed, Vertices, minimumPercent);
}
