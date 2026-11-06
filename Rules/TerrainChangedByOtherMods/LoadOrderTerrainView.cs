using System.Collections.Immutable;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>The terrain and water of the load order as <paramref name="records"/> read them, with the parts earlier runs gave back.</summary>
/// <param name="restored">The parts earlier runs gave back; those of records whose terrain or water was not read are left out.</param>
internal sealed class LoadOrderTerrainView(IPluginRecords records, ImmutableArray<RestoredPart> restored) : ITerrainView
{
    private readonly ILookup<ExteriorCell, RestoredPart> _restoredByCell = restored
        .Select(part => (Cell: records.FindExteriorCell(part.Record), Part: part))
        .Where(static found => found.Cell is not null)
        .ToLookup(static found => found.Cell!.Value, static found => found.Part);

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => records.ReadTerrainVersions(cell);

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => records.ReadWaterCopies(cell);

    public ImmutableArray<RestoredPart> ReadRestoredParts(ExteriorCell cell) => [.. _restoredByCell[cell]];

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => records.ReadTextureMaterialChain(texture);

    public RecordKey? FindLandWorldspace(RecordKey space) => records.FindLandWorldspace(space);
}
