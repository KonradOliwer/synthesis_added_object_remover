using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Terrain heights and navmesh indexes built from Mutagen records the way the reader hands them over.</summary>
internal static class TestGround
{
    public static TerrainHeights NoTerrain() => Terrain(new Dictionary<ExteriorCell, ILandscapeGetter>(), new Dictionary<RecordKey, RecordKey>());

    public static TerrainHeights Terrain(
        IReadOnlyDictionary<ExteriorCell, ILandscapeGetter> landscapes, IReadOnlyDictionary<RecordKey, RecordKey> landWorldspaces)
    {
        var terrain = new LandscapeTerrain(landscapes, landWorldspaces);
        return new TerrainHeights(terrain.ReadHeights, terrain.FindLandWorldspace);
    }

    public static NavmeshIndex Navmeshes(IReadOnlyDictionary<RecordKey, List<CellNavmesh>> navmeshesBySpace) =>
        new(new NavmeshRecords(navmeshesBySpace).ReadTriangles);
}
