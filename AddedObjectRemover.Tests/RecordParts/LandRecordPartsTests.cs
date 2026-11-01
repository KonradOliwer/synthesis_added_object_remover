using AddedObjectRemover.Tests.EndToEnd;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.RecordParts;

/// <summary>LAND points, colours and quarters are judged one by one; the rest holds every unlisted field.</summary>
public class LandRecordPartsTests
{
    private const float HeightTolerance = 8f;
    private const int Side = TerrainGrid.VerticesPerSide;
    private const int LastIndex = Side * Side - 1;
    private const int InnerIndex = 5 * Side + 5;

    private static readonly FormKey LandKey = FormKey.Factory("000800:Base.esm");
    private static readonly LandRecordParts Table = new(HeightTolerance);

    [Fact]
    public void ARaisedPointChangesOnlyThatPoint()
    {
        var raised = Original();
        raised.VertexHeightMap!.HeightMap[Side - 1, Side - 1] = 2;

        Assert.Equal([LastIndex], ChangedPoints(Table.Points, Original(), raised));
        Assert.Empty(ChangedPoints(Table.Colours, Original(), raised));
        Assert.False(Table.Rest.Differs(Original(), raised));
    }

    [Fact]
    public void APointRaisedWithinTheToleranceIsUnchanged()
    {
        var raised = Original();
        raised.VertexHeightMap!.HeightMap[Side - 1, Side - 1] = 1;

        Assert.Empty(ChangedPoints(Table.Points, Original(), raised));
    }

    [Fact]
    public void ATurnedNormalChangesThatPoint()
    {
        var turned = Original();
        turned.VertexNormals![5, 5] = new P3UInt8(1, 2, 3);

        Assert.Equal([InnerIndex], ChangedPoints(Table.Points, Original(), turned));
    }

    [Fact]
    public void ARecolouredPointChangesOnlyItsColour()
    {
        var recoloured = Original();
        recoloured.VertexColors![5, 5] = new P3UInt8(9, 9, 9);

        Assert.Equal([InnerIndex], ChangedPoints(Table.Colours, Original(), recoloured));
        Assert.Empty(ChangedPoints(Table.Points, Original(), recoloured));
    }

    [Fact]
    public void CopyingAPointWritesItsHeightAndNormal()
    {
        var source = Original();
        source.VertexHeightMap!.HeightMap[Side - 1, Side - 1] = 2;
        source.VertexNormals![Side - 1, Side - 1] = new P3UInt8(1, 2, 3);
        var target = LandPoints.Read(Original());
        var from = LandPoints.Read(source);

        Table.Points[LastIndex].CopyInto(target, from);

        Assert.False(Table.Points[LastIndex].Differs(target, from));
        Assert.Equal([LastIndex], ChangedPoints(Table.Points, LandPoints.Read(Original()), target));
    }

    [Fact]
    public void ALayerChangesOnlyItsQuarter()
    {
        var painted = Original();
        painted.Layers.Add(AlphaLayer(Quadrant.TopRight));

        Assert.Equal([TerrainQuadrants.IndexOf(1, 1)], Table.Quarters.Index().Where(entry => entry.Item.Differs(Original(), painted)).Select(entry => entry.Index));
        Assert.False(Table.Rest.Differs(Original(), painted));
    }

    [Fact]
    public void CopyingAQuarterReplacesOnlyItsLayers()
    {
        var source = Original();
        source.Layers.Add(AlphaLayer(Quadrant.TopRight));
        var target = Original();
        target.Layers.Add(AlphaLayer(Quadrant.BottomLeft));
        var topRight = Table.Quarters[TerrainQuadrants.IndexOf(1, 1)];

        topRight.CopyInto(target, source);

        Assert.False(topRight.Differs(target, source));
        Assert.Equal([Quadrant.BottomLeft, Quadrant.TopRight], target.Layers.Select(layer => layer.Header!.Quadrant));
        Assert.IsType<AlphaLayer>(target.Layers[^1]);
    }

    [Fact]
    public void AnUnlistedFieldFallsIntoTheRest()
    {
        var textured = Original();
        textured.Textures = [new FormLink<ILandscapeTextureGetter>(FormKey.Factory("000F00:Base.esm"))];

        Assert.True(Table.Rest.Differs(Original(), textured));
        Assert.Empty(ChangedPoints(Table.Points, Original(), textured));

        var target = Original();
        Table.Rest.CopyInto(target, textured);
        Assert.False(Table.Rest.Differs(target, textured));
    }

    [Fact]
    public void AMissingGridIsARestChangeAndNoPointChange()
    {
        var withoutColours = Original();
        withoutColours.VertexColors = null;

        Assert.True(Table.Rest.Differs(Original(), withoutColours));
        Assert.Empty(ChangedPoints(Table.Colours, Original(), withoutColours));
    }

    [Fact]
    public void TheExcludedFieldsChangeNoPart()
    {
        var restamped = Original();
        restamped.Flags = Landscape.Flag.VertexNormalsHeightMap;
        restamped.EditorID = "Other";

        Assert.False(Table.Rest.Differs(Original(), restamped));
        Assert.Equal(["FormKey", "EditorID", "VersionControl", "FormVersion", "Version2", "Flags"], LandRecordParts.ExcludedFields.ToArray());
        Assert.Equal((int)SkyrimMajorRecord.SkyrimMajorRecordFlag.Compressed, LandRecordParts.ExcludedFlagBits);
    }

    private static List<int> ChangedPoints(IReadOnlyList<RecordPart<LandPoints, LandPoints>> parts, ILandscapeGetter one, ILandscapeGetter other) =>
        ChangedPoints(parts, LandPoints.Read(one), LandPoints.Read(other));

    private static List<int> ChangedPoints(IReadOnlyList<RecordPart<LandPoints, LandPoints>> parts, LandPoints one, LandPoints other) =>
        [.. parts.Index().Where(entry => entry.Item.Differs(one, other)).Select(entry => entry.Index)];

    private static AlphaLayer AlphaLayer(Quadrant quadrant) => new()
    {
        Header = new LayerHeader { Quadrant = quadrant, Texture = new FormLink<ILandscapeTextureGetter>(FormKey.Factory("000F01:Base.esm")), LayerNumber = 1 },
    };

    private static Landscape Original() => new(LandKey, FixtureMod.Release)
    {
        VertexHeightMap = new LandscapeVertexHeightMap { Offset = 0f, HeightMap = new Array2d<sbyte>(new P2Int(Side, Side), default) },
        VertexNormals = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
        VertexColors = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
    };
}
