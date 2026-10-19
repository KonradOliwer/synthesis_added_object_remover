using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>What the decisions need from one land cell's terrain versions, kept instead of the decoded versions.</summary>
/// <param name="ReferenceHeights">The reference terrain's heights; null where it has none.</param>
/// <param name="WinnerHeights">The heights of the version the game shows; null where it has none.</param>
/// <param name="Changes">
/// Per vertex, row by row as the heights; a vertex no counting plugin changed has no changer. Null when no counting plugin
/// changed any vertex of the cell.
/// </param>
internal sealed record CellTerrain(float[]? ReferenceHeights, float[]? WinnerHeights, VertexChange[]? Changes)
{
    public const int VertexCount = TerrainHeights.VerticesPerSide * TerrainHeights.VerticesPerSide;
}

/// <summary>The counting plugins whose own edits made the winner differ from the reference at a vertex, per check.</summary>
/// <param name="HeightChanger">The load-order position of the counting plugin that made the shown height difference; null where none did.</param>
/// <param name="TypeChanger">The load-order position of the counting plugin that made the shown terrain type difference; null where none did.</param>
internal readonly record struct VertexChange(int? HeightChanger, int? TypeChanger)
{
    public bool Any => HeightChanger != null || TypeChanger != null;
}

/// <summary>A counted change of a cell's water.</summary>
/// <param name="Expected">The reference version's water level; null where it has no water.</param>
/// <param name="Shown">The water level the game shows; null where it has no water.</param>
/// <param name="Changer">The load-order position of the counting plugin whose own edit gave the shown water.</param>
/// <param name="Kind">A water height change, or water added or removed.</param>
internal sealed record CellWaterChange(float? Expected, float? Shown, int Changer, TerrainChangeKind Kind)
{
    /// <summary>
    /// A point is covered when the water level is above the land there. It counts as flipped by the water when its covered state
    /// changed and the shown water alone, over the expected land, already changes it; a point that flips only together with the
    /// land change is left to the height check, so one change is not counted twice.
    /// </summary>
    public bool FlipsCoverAt(float expectedLand, float shownLand)
    {
        var wasCovered = IsCoveredBefore(expectedLand);
        return IsCoveredNow(shownLand) != wasCovered && IsCoveredNow(expectedLand) != wasCovered;
    }

    private bool IsCoveredBefore(float land) => Expected is { } level && level > land;

    private bool IsCoveredNow(float land) => Shown is { } level && level > land;
}

/// <summary>A cell under an object's check area, with the vertices of the area in it.</summary>
/// <param name="Terrain">The terrain of the land cell the cell uses, which can be its parent worldspace's.</param>
/// <param name="Water">The counted change of the cell's own water; null when there is none.</param>
internal sealed record CheckedCell(CellTerrain Terrain, CellWaterChange? Water, int[] Vertices)
{
    /// <returns>The cell's water change when it covered or uncovered the vertex; null otherwise, and where either version has no heights.</returns>
    public CellWaterChange? WaterFlipAt(int vertex) =>
        Water is { } water && Terrain.ReferenceHeights is { } expected && Terrain.WinnerHeights is { } shown && water.FlipsCoverAt(expected[vertex], shown[vertex])
            ? water
            : null;
}
