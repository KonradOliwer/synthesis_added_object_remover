using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>The LAND records of the plugin worldspaces, the winning ones and optionally every plugin's own, decoded to height grids on request.</summary>
/// <param name="landscapes">The winning LAND records by the cell of the worldspace that holds them.</param>
/// <param name="winners">The plugin of each of <paramref name="landscapes"/>, by the same cell.</param>
/// <param name="landWorldspaces">Plugin worldspace -> the worldspace whose LAND records it uses (itself, or a parent whose land data it uses).</param>
/// <param name="versions">Every plugin's own LAND record by cell, highest priority first; null when the versions were not asked for.</param>
/// <param name="layerNames">Spells the plugins of the texture keys when the versions' texture layers are decoded; null when the layers were not asked for.</param>
/// <param name="landscapesBelow">The LAND record versions just below a plugin's, by cell; null when the LAND area record versions were not asked for.</param>
internal sealed class LandscapeTerrain(
    IReadOnlyDictionary<ExteriorCell, ILandscapeGetter> landscapes,
    IReadOnlyDictionary<ExteriorCell, PluginName> winners,
    IReadOnlyDictionary<RecordKey, RecordKey> landWorldspaces,
    IReadOnlyDictionary<ExteriorCell, List<LandscapeVersion>>? versions,
    PluginNames? layerNames,
    LandscapesBelow? landscapesBelow)
{
    private readonly IReadOnlySet<RecordKey> _worldspacesWithTerrain = landscapes.Keys.Select(cell => cell.WorldspaceKey).ToHashSet();

    /// <returns>Null where the plugin worldspace is unknown or the worldspace whose LAND records it uses holds none of its own.</returns>
    public RecordKey? FindLandWorldspace(RecordKey pluginWorldspace) =>
        landWorldspaces.TryGetValue(pluginWorldspace, out var landWorldspace) && _worldspacesWithTerrain.Contains(landWorldspace)
            ? landWorldspace
            : null;

    /// <returns>Null where the cell has no LAND record, or a deleted or empty one.</returns>
    public float[]? ReadHeights(ExteriorCell cell) =>
        landscapes.TryGetValue(cell, out var landscape) ? LandHeightReader.Read(landscape) : null;

    /// <returns>Null where no plugin has a LAND record for the cell.</returns>
    public PluginName? ReadWinner(ExteriorCell cell) => winners.TryGetValue(cell, out var plugin) ? plugin : null;

    /// <returns>Null where no plugin below the given plugin has a LAND record for the cell, or it is deleted or empty.</returns>
    public float[]? ReadHeightsBelow(PluginName plugin, ExteriorCell cell)
    {
        if (landscapesBelow is null) throw new InvalidOperationException("The LAND area record versions were not read; ask for them in the read scope.");
        if (landscapesBelow.Plugin != plugin) throw new InvalidOperationException($"The LAND versions were read below {landscapesBelow.Plugin}, not below {plugin}.");
        return landscapesBelow.Landscapes.TryGetValue(cell, out var landscape) ? LandHeightReader.Read(landscape) : null;
    }

    /// <returns>Every plugin's version of the cell in load order (lowest priority first); empty where no plugin has a LAND record for it.</returns>
    public TerrainVersion[] ReadVersions(ExteriorCell cell)
    {
        if (versions is null) throw new InvalidOperationException("The terrain versions were not read; ask for them in the read scope.");
        return versions.TryGetValue(cell, out var cellVersions)
            ? [.. cellVersions.AsEnumerable().Reverse().Select(ToTerrainVersion)]
            : [];
    }

    private TerrainVersion ToTerrainVersion(LandscapeVersion version) => new(
        version.Plugin,
        LandHeightReader.Read(version.Landscape),
        layerNames is null ? null : LandLayerReader.Read(version.Landscape, layerNames));
}

/// <summary>The LAND record versions just below a plugin's, by cell.</summary>
internal sealed record LandscapesBelow(PluginName Plugin, IReadOnlyDictionary<ExteriorCell, ILandscapeGetter> Landscapes);
