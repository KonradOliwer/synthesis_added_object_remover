namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>The counted changes at the vertices of one check area, and which plugin each removal by height or terrain type names.</summary>
/// <param name="Vertices">The vertices of the area in the cells that have a reference terrain, counted once per cell.</param>
/// <param name="HeightChanger">The last loading plugin that changed the height of a vertex; null when none did.</param>
/// <param name="TypeChangesByChanger">Per plugin that changed the terrain type of vertices, how many it changed.</param>
internal sealed record AreaChanges(int Vertices, int? HeightChanger, IReadOnlyDictionary<int, int> TypeChangesByChanger)
{
    public static AreaChanges Count(List<CheckedCell> cells)
    {
        var (vertexCount, heightChanger, typeChanges) = (0, (int?)null, new Dictionary<int, int>());
        foreach (var (terrain, vertices) in cells)
        {
            foreach (var vertex in vertices)
            {
                vertexCount++;
                if (terrain.Changes?[vertex] is not { } change) continue;
                if (change.HeightChanger is { } height) heightChanger = Math.Max(heightChanger ?? height, height);
                if (change.TypeChanger is { } type) typeChanges[type] = typeChanges.GetValueOrDefault(type) + 1;
            }
        }
        return new AreaChanges(vertexCount, heightChanger, typeChanges);
    }

    /// <summary>
    /// The terrain type change counts when at least one vertex changed and the changed vertices make up at least
    /// <paramref name="minimumPercent"/> percent of the area's vertices.
    /// </summary>
    /// <returns>The plugin that changed the type of the most vertices, the later loading one on a tie; null when the change does not count.</returns>
    public int? TypeChanger(int minimumPercent) =>
        ReachesShare(TypeChangesByChanger.Values.Sum(), minimumPercent)
            ? TypeChangesByChanger.MaxBy(changer => (changer.Value, changer.Key)).Key
            : null;

    private bool ReachesShare(int changed, int minimumPercent) => changed > 0 && SharePercent.AtLeast(changed, Vertices, minimumPercent);
}
