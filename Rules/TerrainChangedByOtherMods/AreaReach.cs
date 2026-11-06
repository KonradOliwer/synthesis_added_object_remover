using System.Collections.Immutable;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>Which changes of a check area's cells count, by where the object reaches the ground.</summary>
/// <remarks>
/// The vertices of a cell without checked land count as checked only so that the cell's own water can be compared: reaching the
/// ground there never makes a land change count, in that cell or in another.
/// </remarks>
/// <param name="Land">
/// The object reaches the ground at a checked vertex of a cell with checked land, or the settings check objects at any height: every
/// change of the area counts.
/// </param>
/// <param name="WaterOnlyCells">
/// Without <paramref name="Land"/>, the cells without checked land whose ground the object reaches, by their water cell: only their
/// own water change counts.
/// </param>
internal sealed record AreaReach(bool Land, ImmutableArray<ExteriorCell> WaterOnlyCells)
{
    public bool Any => Land || !WaterOnlyCells.IsEmpty;

    public bool CountsWaterOf(CheckedCell cell) => Land || WaterOnlyCells.Contains(cell.WaterCell);
}
