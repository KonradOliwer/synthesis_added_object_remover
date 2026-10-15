using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

public class TerrainVersionTests
{
    private static readonly RecordKey Worldspace = TestTargets.SpaceKey(0x200);
    private static readonly ExteriorCell Cell = new(Worldspace, 0, 0);
    private static readonly PluginName Early = ModKey.FromNameAndExtension("Early.esp").ToPluginName();
    private static readonly PluginName Late = ModKey.FromNameAndExtension("Late.esp").ToPluginName();
    private static readonly PluginName Deleter = ModKey.FromNameAndExtension("Deleter.esp").ToPluginName();

    private static Landscape Landscape(uint id, float? offset, bool deleted = false) => new(new FormKey(TestTargets.TargetMod, id), SkyrimRelease.SkyrimSE)
    {
        VertexHeightMap = offset is { } value ? new LandscapeVertexHeightMap { Offset = value } : null,
        IsDeleted = deleted,
    };

    private static LandscapeTerrain Terrain(bool versionsRead, params LandscapeVersion[] highestPriorityFirst) => new(
        new Dictionary<ExteriorCell, ILandscapeGetter>(),
        new Dictionary<RecordKey, RecordKey>(),
        versionsRead ? new Dictionary<ExteriorCell, List<LandscapeVersion>> { [Cell] = [.. highestPriorityFirst] } : null,
        layerNames: null);

    [Fact]
    public void VersionsComeInLoadOrderWithEachPluginsOwnHeights()
    {
        var terrain = Terrain(true,
            new LandscapeVersion(Deleter, Landscape(0x302, 3, deleted: true)),
            new LandscapeVersion(Late, Landscape(0x301, null)),
            new LandscapeVersion(Early, Landscape(0x300, 10)));

        var versions = terrain.ReadVersions(Cell);

        Assert.Equal([Early, Late, Deleter], versions.Select(version => version.Plugin));
        Assert.Equal(10 * 8f, versions[0].Heights![0]);
        Assert.Equal(TerrainHeights.VerticesPerSide * TerrainHeights.VerticesPerSide, versions[0].Heights!.Length);
        Assert.Null(versions[1].Heights);
        Assert.Null(versions[2].Heights);
    }

    [Fact]
    public void CellWithoutAnyLandRecordHasNoVersions() =>
        Assert.Empty(Terrain(true).ReadVersions(new ExteriorCell(Worldspace, 5, 5)));

    [Fact]
    public void VersionsMustHaveBeenAskedFor() =>
        Assert.Throws<InvalidOperationException>(() => Terrain(false).ReadVersions(Cell));

    [Fact]
    public void RectangleInsideOneSquareGivesItsFourCorners() =>
        AssertVertices([(0, 0, [0, 1, 33, 34])], new Vector2(10, 10), new Vector2(20, 20));

    [Fact]
    public void RectangleOnlyTouchingASquaresEdgeDoesNotOverlapIt() =>
        AssertVertices([(0, 0, [0, 1, 33, 34])], new Vector2(0, 0), new Vector2(128, 128));

    [Fact]
    public void RectangleWithoutSizeGivesTheSquareItStartsIn() =>
        AssertVertices([(0, 0, [34, 35, 67, 68])], new Vector2(128, 128), new Vector2(128, 128));

    [Fact]
    public void RectangleOverACellEdgeGivesEachCellItsOwnIndices() =>
        AssertVertices(
            [(0, 0, [31, 32, 64, 65]), (1, 0, [0, 1, 33, 34])],
            new Vector2(4000, 10), new Vector2(4200, 20));

    [Fact]
    public void NegativeCoordinatesLieInTheCellsBelowTheOrigin() =>
        AssertVertices([(-1, -1, [31 * 33 + 31, 31 * 33 + 32, 32 * 33 + 31, 32 * 33 + 32])], new Vector2(-10, -10), new Vector2(-5, -5));

    [Fact]
    public void RectangleOverFourCellsGivesFourCellsInXFirstOrder()
    {
        var cells = TerrainGrid.VerticesUnder((new Vector2(4090, 4090), new Vector2(4100, 4100))).Select(vertices => (vertices.CellX, vertices.CellY));
        Assert.Equal([(0, 0), (0, 1), (1, 0), (1, 1)], cells);
    }

    private static void AssertVertices((int CellX, int CellY, int[] Indices)[] expected, Vector2 min, Vector2 max)
    {
        var actual = TerrainGrid.VerticesUnder((min, max)).ToList();
        Assert.Equal(expected.Select(cell => (cell.CellX, cell.CellY)), actual.Select(vertices => (vertices.CellX, vertices.CellY)));
        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i].Indices, actual[i].VertexIndices);
    }
}
