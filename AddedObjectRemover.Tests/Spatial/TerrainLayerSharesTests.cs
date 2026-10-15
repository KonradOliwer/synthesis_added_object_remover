using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Spatial;

/// <summary>The textures a terrain vertex shows follow from its quadrants' base and alpha layers.</summary>
public class TerrainLayerSharesTests
{
    private const float Tolerance = 1e-6f;
    private const int LastVertex = TerrainHeights.VerticesPerSide - 1;
    private const int QuadrantEdge = TerrainQuadrants.VerticesPerSide - 1;

    private static readonly RecordKey Grass = TestTargets.SpaceKey(0x10);
    private static readonly RecordKey Rock = TestTargets.SpaceKey(0x11);
    private static readonly RecordKey Snow = TestTargets.SpaceKey(0x12);

    private static float[] Opacity(int localIndex, float value)
    {
        var opacities = new float[TerrainQuadrants.VertexCount];
        opacities[localIndex] = value;
        return opacities;
    }

    private static TerrainLayers Layers(QuadrantLayers bottomLeft, QuadrantLayers? bottomRight = null) =>
        new([bottomLeft, bottomRight ?? Plain(null), Plain(null), Plain(null)]);

    private static QuadrantLayers Plain(RecordKey? baseTexture) => new(baseTexture, []);

    private static float ShareOf(IEnumerable<TextureShare> shares, RecordKey? texture) =>
        shares.Where(share => share.Texture == texture).Select(share => share.Share).SingleOrDefault();

    [Fact]
    public void ABaseLayerAloneIsTheWholeVertex()
    {
        var shares = TerrainLayerShares.At(Layers(Plain(Grass)), 3, 4);

        Assert.Equal([new TextureShare(Grass, 1f)], shares);
    }

    [Fact]
    public void AQuadrantWithoutABaseLayerShowsTheDefaultTexture()
    {
        var shares = TerrainLayerShares.At(Layers(Plain(null)), 3, 4);

        Assert.Equal([new TextureShare(null, 1f)], shares);
    }

    [Fact]
    public void AnAlphaLayerIsBlendedOverTheBaseByItsOpacityAtTheVertex()
    {
        var local = 4 * TerrainQuadrants.VerticesPerSide + 3;
        var bottomLeft = new QuadrantLayers(Grass, [new TerrainAlphaLayer(Rock, 1, Opacity(local, 0.25f))]);

        var shares = TerrainLayerShares.At(Layers(bottomLeft), 3, 4);

        Assert.Equal(0.75f, ShareOf(shares, Grass), Tolerance);
        Assert.Equal(0.25f, ShareOf(shares, Rock), Tolerance);
    }

    [Fact]
    public void ALayerPaintingNothingAtTheVertexIsLeftOut()
    {
        var bottomLeft = new QuadrantLayers(Grass, [new TerrainAlphaLayer(Rock, 1, Opacity(0, 1f))]);

        var shares = TerrainLayerShares.At(Layers(bottomLeft), 3, 4);

        Assert.Equal([new TextureShare(Grass, 1f)], shares);
    }

    [Fact]
    public void AnAlphaLayerAtFullOpacityCoversWhatLiesBelow()
    {
        var bottomLeft = new QuadrantLayers(Grass, [new TerrainAlphaLayer(Rock, 1, Opacity(0, 1f))]);

        var shares = TerrainLayerShares.At(Layers(bottomLeft), 0, 0);

        Assert.Equal([new TextureShare(Rock, 1f)], shares);
    }

    [Fact]
    public void LayersAreBlendedFromTheLowestLayerNumberUpWhateverTheStoredOrder()
    {
        var upper = new TerrainAlphaLayer(Snow, 2, Opacity(0, 0.5f));
        var lower = new TerrainAlphaLayer(Rock, 1, Opacity(0, 0.5f));

        var shares = TerrainLayerShares.At(Layers(new QuadrantLayers(Grass, [upper, lower])), 0, 0);

        Assert.Equal(0.25f, ShareOf(shares, Grass), Tolerance);
        Assert.Equal(0.25f, ShareOf(shares, Rock), Tolerance);
        Assert.Equal(0.5f, ShareOf(shares, Snow), Tolerance);
    }

    [Fact]
    public void ALayerRepeatingTheBaseTextureAddsToItsShare()
    {
        var bottomLeft = new QuadrantLayers(Grass, [new TerrainAlphaLayer(Grass, 1, Opacity(0, 0.5f))]);

        var shares = TerrainLayerShares.At(Layers(bottomLeft), 0, 0);

        Assert.Equal([new TextureShare(Grass, 1f)], shares);
    }

    [Fact]
    public void AVertexOnTheEdgeBetweenTwoQuadrantsAveragesTheirShares()
    {
        var shares = TerrainLayerShares.At(Layers(Plain(Grass), Plain(Rock)), QuadrantEdge, 3);

        Assert.Equal(0.5f, ShareOf(shares, Grass), Tolerance);
        Assert.Equal(0.5f, ShareOf(shares, Rock), Tolerance);
    }

    [Fact]
    public void AVertexInTheLastRowAndColumnBelongsToTheTopRightQuadrant()
    {
        var layers = new TerrainLayers([Plain(Grass), Plain(Grass), Plain(Grass), Plain(Snow)]);

        Assert.Equal([new TextureShare(Snow, 1f)], TerrainLayerShares.At(layers, LastVertex, LastVertex));
    }

    [Fact]
    public void TexturesComeOutDefaultFirstThenInKeyOrder()
    {
        var layers = new TerrainLayers([Plain(Snow), Plain(null), Plain(Grass), Plain(Rock)]);

        var shares = TerrainLayerShares.At(layers, QuadrantEdge, QuadrantEdge);

        Assert.Equal(new RecordKey?[] { null, Grass, Rock, Snow }, shares.Select(share => share.Texture));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, LastVertex + 1)]
    public void AVertexOutsideTheCellIsABug(int vertexX, int vertexY)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainLayerShares.At(Layers(Plain(Grass)), vertexX, vertexY));
    }
}
