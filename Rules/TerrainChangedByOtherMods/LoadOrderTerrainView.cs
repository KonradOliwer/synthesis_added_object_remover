using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>The terrain and water of the load order as <paramref name="records"/> read them.</summary>
internal sealed class LoadOrderTerrainView(IPluginRecords records) : ITerrainView
{
    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => records.ReadTerrainVersions(cell);

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => records.ReadWaterCopies(cell);

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => records.ReadTextureMaterialChain(texture);

    public RecordKey? FindLandWorldspace(RecordKey space) => records.FindLandWorldspace(space);
}
