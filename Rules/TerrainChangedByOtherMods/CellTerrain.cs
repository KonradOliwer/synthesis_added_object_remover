using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>What the decisions need from one land cell's terrain versions, kept instead of the decoded versions.</summary>
/// <param name="AcceptedHeights">
/// Each vertex's height in its accepted version, or the shown height where that version holds none or the vertex has none; null
/// where neither holds a height.
/// </param>
/// <param name="WinnerHeights">The heights of the version the game shows; null where it has none.</param>
/// <param name="Changes">
/// Per vertex, row by row as the heights; a vertex whose accepted values the game shows, or that has none, has no changer. Null when
/// no vertex of the cell changed.
/// </param>
/// <param name="UncheckedVertices">
/// Per vertex, row by row as the heights, whether it has neither an accepted height nor an accepted terrain type, so that nothing is
/// checked there; null when every vertex is checked.
/// </param>
/// <param name="TypeUnchecked">
/// Per vertex, row by row as the heights, whether it has no accepted terrain type; null when every vertex has one.
/// </param>
/// <param name="ShownOnly">
/// No vertex of the cell has an accepted height or terrain type: the ground is taken as the game shows it, every vertex counts as
/// checked, and only the cell's water can be compared over it.
/// </param>
internal sealed record CellTerrain(
    float[]? AcceptedHeights,
    float[]? WinnerHeights,
    VertexChange[]? Changes,
    bool[]? UncheckedVertices,
    bool[]? TypeUnchecked,
    bool ShownOnly)
{
    /// <summary>The ground of a cell none of whose vertices has an accepted height or terrain type, as the game shows it.</summary>
    public static CellTerrain OfShownGround(float[] shown) =>
        new(shown, shown, Changes: null, UncheckedVertices: null, TypeUnchecked: null, ShownOnly: true);

    public bool IsCheckedAt(int vertex) => UncheckedVertices?[vertex] != true;

    public bool IsTypeCheckedAt(int vertex) => !ShownOnly && TypeUnchecked?[vertex] != true;
}

/// <summary>
/// The plugins named for the changes of a vertex's accepted values, per check; <see cref="TerrainPlugins.PatchOnlyChange"/> where
/// only the patch holds the change.
/// </summary>
/// <param name="HeightChanger">The load-order position of the plugin named for the changed height; null where the height is unchanged or not checked.</param>
/// <param name="TypeChanger">The load-order position of the plugin named for the changed terrain type; null where the type is unchanged or not checked.</param>
internal readonly record struct VertexChange(int? HeightChanger, int? TypeChanger)
{
    public bool Any => HeightChanger != null || TypeChanger != null;
}

/// <summary>The outcome of comparing a cell's water.</summary>
/// <param name="IsChecked">Every part the cell's water is made of has an accepted version.</param>
/// <param name="Change">The counted change of the water; null when there is none.</param>
internal readonly record struct WaterComparison(bool IsChecked, CellWaterChange? Change)
{
    public static WaterComparison NotChecked => default;
}

/// <summary>A counted change of a cell's water.</summary>
/// <param name="Accepted">The accepted water level; null where the accepted water is none.</param>
/// <param name="Shown">The water level the game shows; null where it has no water.</param>
/// <param name="Changer">
/// The load-order position of the plugin named for the change; <see cref="TerrainPlugins.PatchOnlyChange"/> where only the patch
/// holds it.
/// </param>
/// <param name="Kind">A water height change, or water added or removed.</param>
internal sealed record CellWaterChange(float? Accepted, float? Shown, int Changer, TerrainChangeKind Kind)
{
    /// <summary>
    /// A point is covered when the water level is above the land there. It counts as flipped by the water when its covered state
    /// changed and the shown water alone, over the accepted land, already changes it; a point that flips only together with the
    /// land change is left to the height check, so one change is not counted twice.
    /// </summary>
    public bool FlipsCoverAt(float acceptedLand, float shownLand)
    {
        var wasCovered = IsCoveredBefore(acceptedLand);
        return IsCoveredNow(shownLand) != wasCovered && IsCoveredNow(acceptedLand) != wasCovered;
    }

    private bool IsCoveredBefore(float land) => Accepted is { } level && level > land;

    private bool IsCoveredNow(float land) => Shown is { } level && level > land;
}

/// <summary>A cell under an object's check area, with the vertices of the area in it.</summary>
/// <param name="LandCell">The cell of the worldspace whose land the cell uses, which can be its parent worldspace.</param>
/// <param name="WaterCell">The cell itself, in the object's own worldspace.</param>
/// <param name="Terrain">The terrain of <paramref name="LandCell"/>.</param>
/// <param name="Water">The counted change of the cell's own water; null when there is none.</param>
internal sealed record CheckedCell(ExteriorCell LandCell, ExteriorCell WaterCell, CellTerrain Terrain, CellWaterChange? Water, int[] Vertices)
{
    /// <returns>The cell's water change when it covered or uncovered the vertex; null otherwise, and where either version has no heights.</returns>
    public CellWaterChange? WaterFlipAt(int vertex) =>
        Water is { } water && Terrain.AcceptedHeights is { } accepted && Terrain.WinnerHeights is { } shown && water.FlipsCoverAt(accepted[vertex], shown[vertex])
            ? water
            : null;
}
