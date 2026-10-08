using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>The winning LAND records of the target worldspaces, decoded to height grids on request.</summary>
/// <param name="landscapes">LAND records by the cell of the worldspace that holds them.</param>
/// <param name="landWorldspaces">Target worldspace -> the worldspace whose LAND records it uses (itself, or a parent whose land data it uses).</param>
internal sealed class LandscapeTerrain(
    IReadOnlyDictionary<ExteriorCell, ILandscapeGetter> landscapes,
    IReadOnlyDictionary<RecordKey, RecordKey> landWorldspaces)
{
    private readonly IReadOnlySet<RecordKey> _worldspacesWithTerrain = landscapes.Keys.Select(cell => cell.WorldspaceKey).ToHashSet();

    /// <returns>Null where the target worldspace is unknown or the worldspace whose LAND records it uses holds none of its own.</returns>
    public RecordKey? FindLandWorldspace(RecordKey targetWorldspace) =>
        landWorldspaces.TryGetValue(targetWorldspace, out var landWorldspace) && _worldspacesWithTerrain.Contains(landWorldspace)
            ? landWorldspace
            : null;

    /// <returns>Null where the cell has no LAND record, or a deleted or empty one.</returns>
    public float[]? ReadHeights(ExteriorCell cell) =>
        landscapes.TryGetValue(cell, out var landscape) ? LandHeightReader.Read(landscape) : null;
}
