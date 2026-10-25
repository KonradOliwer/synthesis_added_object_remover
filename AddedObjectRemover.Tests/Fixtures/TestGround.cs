using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Terrain heights and navmesh indexes built from Mutagen records the way the reader hands them over.</summary>
internal static class TestGround
{
    public static TerrainHeights NoTerrain() => Terrain(new Dictionary<ExteriorCell, ILandscapeGetter>(), new Dictionary<RecordKey, RecordKey>());

    /// <summary>Flat land at height 0 in <see cref="TestTargets.Space"/>'s cell (0, 0), which reaches from 0 to 4096 on both axes.</summary>
    public static TerrainHeights FlatAtZero() =>
        Terrain(
            new Dictionary<ExteriorCell, ILandscapeGetter>
            {
                [new ExteriorCell(TestTargets.Space, 0, 0)] = new Landscape(new FormKey(TestTargets.TargetMod, 0x300), SkyrimRelease.SkyrimSE) { VertexHeightMap = new LandscapeVertexHeightMap() },
            },
            new Dictionary<RecordKey, RecordKey> { [TestTargets.Space] = TestTargets.Space });

    public static TerrainHeights Terrain(
        IReadOnlyDictionary<ExteriorCell, ILandscapeGetter> landscapes, IReadOnlyDictionary<RecordKey, RecordKey> landWorldspaces)
    {
        var terrain = new LandscapeTerrain(landscapes, landWorldspaces, versions: null, layerNames: null);
        return new TerrainHeights(terrain.ReadHeights, terrain.FindLandWorldspace);
    }

    public static NavmeshIndex Navmeshes(IReadOnlyDictionary<RecordKey, List<CellNavmesh>> navmeshesBySpace) =>
        new(new NavmeshRecords(navmeshesBySpace).ReadTriangles);
}
