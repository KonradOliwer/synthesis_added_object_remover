using AddedObjectRemover.Tests.EndToEnd;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.RecordParts;

/// <summary>Each part of a cell record is seen alone, regions are judged one by one, and the rest holds every unlisted field.</summary>
public class CellRecordPartsTests
{
    private const float WaterHeightTolerance = 16f;
    private const ushort OlderFormVersion = 43;
    private const ushort NewerFormVersion = 44;

    private static readonly FormKey CellKey = FormKey.Factory("000800:Base.esm");
    private static readonly FormKey FirstRegion = FormKey.Factory("000C00:Base.esm");
    private static readonly FormKey SecondRegion = FormKey.Factory("000C01:Base.esm");
    private static readonly CellRecordParts Table = new(WaterHeightTolerance);

    /// <summary>One change per case, with the only part it shows in; null when it shows in none.</summary>
    public static TheoryData<string, CellPart?> Changes() => new()
    {
        { "lighting", CellPart.Lighting },
        { "lighting template", CellPart.Lighting },
        { "image space", CellPart.ImageSpace },
        { "acoustic space", CellPart.AcousticSpace },
        { "music", CellPart.Music },
        { "has water", CellPart.WaterLevel },
        { "water height", CellPart.WaterLevel },
        { "water type", CellPart.WaterLook },
        { "water noise texture", CellPart.WaterLook },
        { "sky from region", CellPart.SkyAndWeatherFromRegion },
        { "name", CellPart.Name },
        { "owner", CellPart.Ownership },
        { "faction rank", CellPart.Ownership },
        { "encounter zone", CellPart.EncounterZone },
        { "location", CellPart.Location },
        { "cant travel", CellPart.CantTravelFromHere },
        { "no lod water", CellPart.NoLodWater },
        { "public area", CellPart.PublicArea },
        { "hand changed", CellPart.HandChanged },
        { "show sky", CellPart.ShowSky },
        { "use sky lighting", CellPart.UseSkyLighting },
        { "off limits", CellPart.OffLimits },
        { "cant wait", CellPart.CantWait },
        { "lock list (on no list)", CellPart.RestOfCell },
        { "occlusion bytes (on no list)", CellPart.RestOfCell },
        { "unnamed cell flag", CellPart.RestOfCell },
        { "editor id (excluded)", null },
        { "grid (excluded)", null },
        { "timestamp (excluded)", null },
        { "interior flag (excluded)", null },
        { "persistent flag (excluded)", null },
        { "child record (excluded)", null },
        { "water height within the tolerance", null },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void AChangeShowsOnlyInItsOwnPart(string change, CellPart? expected) =>
        Assert.Equal(expected is { } part ? [part] : [], ChangedParts(Original(), Changed(change)).Select(key => key.Part));

    [Theory]
    [MemberData(nameof(Changes))]
    public void CopyingAPartWritesItAndNothingElse(string change, CellPart? part)
    {
        if (part is not { } changed) return;
        var source = Changed(change);
        var target = Original();
        var compare = Compare([target, source], new CellPartKey(changed, Region: null));

        compare.CopyInto(target, source);

        Assert.False(compare.Differs(target, source));
        Assert.Equal([changed], ChangedParts(Original(), target).Select(key => key.Part));
    }

    [Fact]
    public void EachRegionIsAPartOfItsOwn()
    {
        var earlier = Original();
        earlier.Regions = [new FormLink<IRegionGetter>(FirstRegion)];
        var later = Original();
        later.Regions = [new FormLink<IRegionGetter>(FirstRegion), new FormLink<IRegionGetter>(SecondRegion)];

        Assert.Equal([new CellPartKey(CellPart.Region, SecondRegion)], ChangedParts(earlier, later));
    }

    [Fact]
    public void CopyingARegionAddsOrRemovesOnlyThatRegion()
    {
        var withBoth = Original();
        withBoth.Regions = [new FormLink<IRegionGetter>(FirstRegion), new FormLink<IRegionGetter>(SecondRegion)];
        var withFirst = Original();
        withFirst.Regions = [new FormLink<IRegionGetter>(FirstRegion)];
        var second = Compare([withFirst, withBoth], new CellPartKey(CellPart.Region, SecondRegion));

        second.CopyInto(withFirst, withBoth);
        Assert.Equal([FirstRegion, SecondRegion], withFirst.Regions!.Select(link => link.FormKey));

        second.CopyInto(withBoth, Original());
        Assert.Equal([FirstRegion], withBoth.Regions!.Select(link => link.FormKey));
    }

    [Fact]
    public void ThePartsAreListedWithTheRegionsOfEveryVersionAndTheRestLast()
    {
        var withRegion = Original();
        withRegion.Regions = [new FormLink<IRegionGetter>(FirstRegion)];

        var keys = Table.For([Original(), withRegion]).Select(entry => entry.Key).ToList();

        Assert.Equal(Enum.GetValues<CellPart>().Order(), keys.Select(key => key.Part).Order());
        Assert.Equal(new CellPartKey(CellPart.Region, FirstRegion), keys.Single(key => key.Part == CellPart.Region));
        Assert.Equal(CellPart.RestOfCell, keys[^1].Part);
    }

    [Fact]
    public void RawBytesAreWrittenOnlyIntoTheSameFormVersion()
    {
        var source = Changed("occlusion bytes (on no list)");
        source.FormVersion = OlderFormVersion;
        var otherFormat = Original();
        otherFormat.FormVersion = NewerFormVersion;
        var sameFormat = Original();
        sameFormat.FormVersion = OlderFormVersion;
        var rest = Compare([source], new CellPartKey(CellPart.RestOfCell, Region: null));

        Assert.False(RawByteFormat.AllowsCopy(rest, source, otherFormat));
        Assert.True(RawByteFormat.AllowsCopy(rest, source, sameFormat));
        Assert.True(RawByteFormat.AllowsCopy(Compare([source], new CellPartKey(CellPart.Music, Region: null)), source, otherFormat));
    }

    [Fact]
    public void TheExclusionListIsTheApprovedOne()
    {
        Assert.Equal(
            ["FormKey", "EditorID", "VersionControl", "FormVersion", "Version2",
                "Timestamp", "UnknownGroupData", "PersistentTimestamp", "PersistentUnknownGroupData", "TemporaryTimestamp", "TemporaryUnknownGroupData",
                "Persistent", "Temporary", "NavigationMeshes", "Landscape", "Grid"],
            CellRecordParts.ExcludedFields.ToArray());
        Assert.Equal((int)Cell.Flag.IsInteriorCell, CellRecordParts.ExcludedFlagBits);
        Assert.Equal((int)Cell.MajorFlag.Persistent | (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.Compressed, CellRecordParts.ExcludedMajorFlagBits);
    }

    private static RecordPart<ICellGetter, ICell> Compare(IReadOnlyList<ICellGetter> versions, CellPartKey key) =>
        Table.For(versions).Single(entry => entry.Key == key).Compare;

    private static List<CellPartKey> ChangedParts(ICellGetter one, ICellGetter other) =>
        [.. Table.For([one, other]).Where(entry => entry.Compare.Differs(one, other)).Select(entry => entry.Key)];

    private static Cell Original() => new(CellKey, FixtureMod.Release)
    {
        EditorID = "Village",
        Flags = Cell.Flag.HasWater,
        WaterHeight = -100f,
        Grid = new CellGrid { Point = new P2Int(1, 2) },
        Lighting = new CellLighting { FogNear = 10f },
    };

    private static Cell Changed(string change)
    {
        var cell = Original();
        switch (change)
        {
            case "lighting": cell.Lighting!.FogNear = 20f; break;
            case "lighting template": cell.LightingTemplate.SetTo(FormKey.Factory("000D00:Base.esm")); break;
            case "image space": cell.ImageSpace.SetTo(FormKey.Factory("000D01:Base.esm")); break;
            case "acoustic space": cell.AcousticSpace.SetTo(FormKey.Factory("000D02:Base.esm")); break;
            case "music": cell.Music.SetTo(FormKey.Factory("000D03:Base.esm")); break;
            case "has water": cell.Flags &= ~Cell.Flag.HasWater; break;
            case "water height": cell.WaterHeight = 0f; break;
            case "water type": cell.Water.SetTo(FormKey.Factory("000D04:Base.esm")); break;
            case "water noise texture": cell.WaterNoiseTexture = "noise.dds"; break;
            case "sky from region": cell.SkyAndWeatherFromRegion.SetTo(FormKey.Factory("000D05:Base.esm")); break;
            case "name": cell.Name = "Village"; break;
            case "owner": cell.Owner.SetTo(FormKey.Factory("000D06:Base.esm")); break;
            case "faction rank": cell.FactionRank = 2; break;
            case "encounter zone": cell.EncounterZone.SetTo(FormKey.Factory("000D07:Base.esm")); break;
            case "location": cell.Location.SetTo(FormKey.Factory("000D08:Base.esm")); break;
            case "cant travel": cell.Flags |= Cell.Flag.CantTravelFromHere; break;
            case "no lod water": cell.Flags |= Cell.Flag.NoLodWater; break;
            case "public area": cell.Flags |= Cell.Flag.PublicArea; break;
            case "hand changed": cell.Flags |= Cell.Flag.HandChanged; break;
            case "show sky": cell.Flags |= Cell.Flag.ShowSky; break;
            case "use sky lighting": cell.Flags |= Cell.Flag.UseSkyLighting; break;
            case "off limits": cell.MajorFlags |= Cell.MajorFlag.OffLimits; break;
            case "cant wait": cell.MajorFlags |= Cell.MajorFlag.CantWait; break;
            case "lock list (on no list)": cell.LockList.SetTo(FormKey.Factory("000D09:Base.esm")); break;
            case "occlusion bytes (on no list)": cell.OcclusionData = new byte[] { 1, 2 }; break;
            case "unnamed cell flag": cell.Flags |= (Cell.Flag)0x1000; break;
            case "editor id (excluded)": cell.EditorID = "Town"; break;
            case "grid (excluded)": cell.Grid = new CellGrid { Point = new P2Int(3, 4) }; break;
            case "timestamp (excluded)": cell.Timestamp = 7; break;
            case "interior flag (excluded)": cell.Flags |= Cell.Flag.IsInteriorCell; break;
            case "persistent flag (excluded)": cell.MajorFlags |= Cell.MajorFlag.Persistent; break;
            case "child record (excluded)": cell.Temporary.Add(new PlacedObject(FormKey.Factory("000E00:Base.esm"), FixtureMod.Release)); break;
            case "water height within the tolerance": cell.WaterHeight = -90f; break;
            default: throw new ArgumentException(change, nameof(change));
        }
        return cell;
    }
}
