using System.Collections.Immutable;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What one pass plans to write into a LAND record.</summary>
/// <param name="Heights">The heights it writes; null when none goes back.</param>
/// <param name="Textures">The quarters whose texture layers go back.</param>
/// <param name="Rest">The rest of the record goes back, with which grids it holds.</param>
internal sealed record PlannedLand(float[]? Heights, QuarterSet Textures, bool Rest);

/// <summary>
/// The terrain and water of the load order with the patch's versions replaced by the ones one pass plans to write, as the next
/// patcher reads them: in a cell the pass writes, the patch's version (added last where the patch has none) is a copy of the winning
/// version holding the stored heights, the earlier quarters' texture layers and, with the rest going back, the earlier version's height
/// map where only one of the two holds one; or the earlier water. The parts the pass gives back count as given back, as the next patcher
/// reads them from the restored parts. Thread-safe.
/// </summary>
/// <param name="lands">By the cell of the land worldspace that holds the LAND record.</param>
/// <param name="water">The water fields going back, by the cell of its own worldspace.</param>
/// <param name="givenBack">The parts the pass gives back, by the cell of <paramref name="lands"/> or <paramref name="water"/> they are of.</param>
internal sealed class PlannedTerrainView(
    ITerrainView loadOrder,
    PluginName patch,
    PluginName cleanedMod,
    IReadOnlyDictionary<ExteriorCell, PlannedLand> lands,
    IReadOnlyDictionary<ExteriorCell, CellWaterCopy> water,
    ILookup<ExteriorCell, RestoredPart> givenBack)
    : ITerrainView
{
    /// <summary>The cells whose land or water the pass writes.</summary>
    public IReadOnlySet<ExteriorCell> CellsWritten() => lands.Keys.Concat(water.Keys).ToHashSet();

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell)
    {
        var versions = loadOrder.ReadTerrainVersions(cell);
        return lands.TryGetValue(cell, out var land) ? [.. versions.Where(version => version.Plugin != patch), Planned(versions, land)] : versions;
    }

    public WaterCopies ReadWaterCopies(ExteriorCell cell)
    {
        var copies = loadOrder.ReadWaterCopies(cell);
        return water.TryGetValue(cell, out var earlier) ? copies with { Cell = [.. copies.Cell.Where(copy => copy.Plugin != patch), earlier with { Plugin = patch }] } : copies;
    }

    public ImmutableArray<RestoredPart> ReadRestoredParts(ExteriorCell cell) => [.. loadOrder.ReadRestoredParts(cell), .. givenBack[cell]];

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => loadOrder.ReadTextureMaterialChain(texture);

    public RecordKey? FindLandWorldspace(RecordKey space) => loadOrder.FindLandWorldspace(space);

    /// <summary>The winning version with the planned heights as stored and the earlier version's layers in the quarters going back.</summary>
    private TerrainVersion Planned(TerrainVersion[] versions, PlannedLand land)
    {
        var heights = land.Heights is { } planned
            ? LandHeightEncoder.Stored(planned) ?? throw new InvalidOperationException("The restore planned heights the height map cannot store.")
            : HeightsWithoutPlannedOnes(versions, land);
        return new TerrainVersion(patch, heights, land.Textures.IsEmpty ? versions[^1].Layers : LayersOf(versions, land.Textures));
    }

    /// <summary>
    /// The winner's heights; with the rest of the record going back, which copies a height map the winner and the earlier version do
    /// not both hold, the earlier version's heights, or none, where only one of them holds one.
    /// </summary>
    private float[]? HeightsWithoutPlannedOnes(TerrainVersion[] versions, PlannedLand land)
    {
        var winner = versions[^1].Heights;
        if (!land.Rest) return winner;
        var earlier = EarlierThanCleanedMod(versions).Heights;
        return (winner is null) == (earlier is null) ? winner : earlier;
    }

    /// <returns>Null where the read scope asked for no layers, as for every other version.</returns>
    private TerrainLayers? LayersOf(TerrainVersion[] versions, QuarterSet textures) =>
        (versions[^1].Layers, EarlierThanCleanedMod(versions).Layers) is ({ } winning, { } earlier) ? winning.WithQuadrantsOf(earlier, textures.Quarters) : versions[^1].Layers;

    /// <summary>The version just below the cleaned mod's last one, whose values go back.</summary>
    private TerrainVersion EarlierThanCleanedMod(TerrainVersion[] versions)
    {
        var cleanedMods = Array.FindLastIndex(versions, version => version.Plugin == cleanedMod);
        return cleanedMods > 0 ? versions[cleanedMods - 1] : throw new InvalidOperationException("A LAND record the restore writes has no version below the cleaned mod's.");
    }
}
