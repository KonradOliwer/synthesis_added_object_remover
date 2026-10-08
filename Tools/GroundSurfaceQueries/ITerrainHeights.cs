using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Cache C13: the terrain height of exterior cells, each cell's height grid read once. Thread-safe.</summary>
public interface ITerrainHeights
{
    /// <summary>False for interior cells and worldspaces whose land has no land data.</summary>
    bool HasTerrain(RecordKey spaceKey);

    /// <param name="worldspaceKey">A worldspace with terrain (<see cref="HasTerrain"/>).</param>
    /// <returns>False where the worldspace has no terrain (no, a deleted, or an empty land record).</returns>
    bool TryGetHeight(RecordKey worldspaceKey, Vector2 position, out float height);

    /// <param name="worldspaceKey">A worldspace with terrain (<see cref="HasTerrain"/>).</param>
    /// <returns>False where the cell has no terrain (no, a deleted, or an empty land record).</returns>
    bool HasHeights(RecordKey worldspaceKey, int cellX, int cellY);
}
