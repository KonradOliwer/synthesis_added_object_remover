using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>Compares every plugin's terrain version of a cell with the cell's reference terrain, vertex by vertex. Thread-safe.</summary>
internal sealed class CellTerrainComparison(TerrainPlugins plugins, TerrainTypes types, ChangedTerrainSettings settings)
{
    private readonly TerrainDifference _difference = new(settings);

    /// <param name="versions">The cell's versions in load order.</param>
    /// <returns>Null when none of the reference groups has a version of the cell.</returns>
    public CellTerrain? Compare(TerrainVersion[] versions)
    {
        if (plugins.ReferenceIn(versions) is not { } referenceIndex) return null;

        var winnerIndex = versions.Length - 1;
        var reference = ToCompared(versions[referenceIndex]);
        var winner = winnerIndex == referenceIndex ? reference : ToCompared(versions[winnerIndex]);
        // Only the versions loading after the reference can change it: the reference overrides the earlier ones.
        var changers = Enumerable.Range(referenceIndex + 1, winnerIndex - referenceIndex)
            .Where(index => plugins.CountsAsChanging(versions[index].Plugin))
            .Select(index => (Version: index == winnerIndex ? winner : ToCompared(versions[index]), Position: plugins.PositionOf(versions[index].Plugin)))
            .ToArray();
        return new CellTerrain(reference.Heights, winner.Heights, ChangesOf(reference, winner, changers));
    }

    private ComparedVersion ToCompared(TerrainVersion version) =>
        new(version.Plugin, version.Heights, settings.CountTerrainTypeChanges ? types.AtEveryVertex(version) : null);

    /// <param name="changers">The counting versions loading after the reference, with their load-order positions.</param>
    private VertexChange[]? ChangesOf(ComparedVersion reference, ComparedVersion winner, (ComparedVersion Version, int Position)[] changers)
    {
        VertexChange[]? changes = null;
        for (var vertex = 0; vertex < CellTerrain.VertexCount; vertex++)
        {
            var shown = _difference.At(reference, winner, vertex);
            if (!shown.Any || LastChangerAt(reference, changers, vertex) is not { } lastChanger) continue;
            changes ??= new VertexChange[CellTerrain.VertexCount];
            changes[vertex] = new VertexChange(shown, lastChanger);
        }
        return changes;
    }

    /// <returns>The highest load-order position among the changers whose own version differs at the vertex; null where none does.</returns>
    private int? LastChangerAt(ComparedVersion reference, (ComparedVersion Version, int Position)[] changers, int vertex)
    {
        int? last = null;
        foreach (var (version, position) in changers)
        {
            if (_difference.At(reference, version, vertex).Any && (last is not { } found || position > found)) last = position;
        }
        return last;
    }
}
