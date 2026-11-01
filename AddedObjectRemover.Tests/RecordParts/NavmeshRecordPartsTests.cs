using AddedObjectRemover.Tests.EndToEnd;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.RecordParts;

/// <summary>A navmesh's geometry and presence are judged apart, and its map entry is judged and copied whole.</summary>
public class NavmeshRecordPartsTests
{
    private const ushort OlderFormVersion = 43;
    private const ushort NewerFormVersion = 44;

    private static readonly FormKey Navmesh = FormKey.Factory("000800:Base.esm");
    private static readonly FormKey OtherNavmesh = FormKey.Factory("000801:Base.esm");

    [Fact]
    public void AMovedVertexChangesTheGeometryAlone()
    {
        var moved = Original();
        moved.Data!.Vertices[0] = new P3Float(5, 0, 0);

        Assert.True(NavmeshRecordParts.Geometry.Differs(Original(), moved));
        Assert.False(NavmeshRecordParts.Presence.Differs(Original(), moved));
    }

    [Fact]
    public void AnEdgeLinkChangesTheGeometry()
    {
        var linked = Original();
        linked.Data!.Triangles[0].Flags |= NavmeshTriangle.Flag.EdgeLink_0_1;
        linked.Data.Triangles[0].EdgeLink_0_1 = 0;
        linked.Data.EdgeLinks.Add(new EdgeLink { Mesh = new FormLink<INavigationMeshGetter>(OtherNavmesh), TriangleIndex = 3 });

        Assert.True(NavmeshRecordParts.Geometry.Differs(Original(), linked));
    }

    [Fact]
    public void TheRawBytesAndOtherRecordFlagsBelongToTheGeometry()
    {
        var withBytes = Original();
        withBytes.NNAM = new byte[] { 1 };
        var flagged = Original();
        flagged.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.Ignored;

        Assert.True(NavmeshRecordParts.Geometry.Differs(Original(), withBytes));
        Assert.True(NavmeshRecordParts.Geometry.Differs(Original(), flagged));
    }

    [Fact]
    public void DeletingChangesThePresenceAlone()
    {
        var deleted = Original();
        deleted.IsDeleted = true;

        Assert.True(NavmeshRecordParts.Presence.Differs(Original(), deleted));
        Assert.False(NavmeshRecordParts.Geometry.Differs(Original(), deleted));
    }

    [Fact]
    public void TheExcludedFieldsChangeNoPart()
    {
        var restamped = Original();
        restamped.EditorID = "Other";
        restamped.FormVersion = NewerFormVersion;
        restamped.IsCompressed = true;

        Assert.False(NavmeshRecordParts.Geometry.Differs(Original(), restamped));
        Assert.False(NavmeshRecordParts.Presence.Differs(Original(), restamped));
        Assert.Equal(["FormKey", "EditorID", "VersionControl", "FormVersion", "Version2"], NavmeshRecordParts.ExcludedFields);
        Assert.Equal((int)SkyrimMajorRecord.SkyrimMajorRecordFlag.Compressed, NavmeshRecordParts.ExcludedFlagBits);
    }

    [Fact]
    public void CopyingTheGeometryKeepsTheDeletedState()
    {
        var source = Original();
        source.Data!.Vertices[0] = new P3Float(5, 0, 0);
        var target = Original();
        target.IsDeleted = true;

        NavmeshRecordParts.Geometry.CopyInto(target, source);

        Assert.False(NavmeshRecordParts.Geometry.Differs(target, source));
        Assert.True(target.IsDeleted);
    }

    [Fact]
    public void GeometryRawBytesAreWrittenOnlyIntoTheSameFormVersion()
    {
        var source = Original();
        source.FormVersion = OlderFormVersion;
        source.PNAM = new byte[] { 1 };
        var otherFormat = Original();
        otherFormat.FormVersion = NewerFormVersion;
        var gridOnly = Original();
        gridOnly.FormVersion = OlderFormVersion;
        gridOnly.Data!.NavmeshGrid = new byte[] { 1 };

        Assert.False(RawByteFormat.AllowsCopy(NavmeshRecordParts.Geometry, source, otherFormat));
        Assert.False(RawByteFormat.AllowsCopy(NavmeshRecordParts.Geometry, gridOnly, otherFormat));
        Assert.True(RawByteFormat.AllowsCopy(NavmeshRecordParts.Presence, source, otherFormat));
    }

    [Fact]
    public void AMapEntryIsComparedWhole()
    {
        var moved = Entry(Navmesh);
        moved.Point = new P3Float(1, 2, 3);
        var merged = Entry(Navmesh);
        merged.MergedTo.Add(new FormLink<INavigationMeshGetter>(OtherNavmesh));

        Assert.False(NavmeshRecordParts.MapEntry.Differs(Entry(Navmesh), Entry(Navmesh)));
        Assert.True(NavmeshRecordParts.MapEntry.Differs(Entry(Navmesh), moved));
        Assert.True(NavmeshRecordParts.MapEntry.Differs(Entry(Navmesh), merged));
    }

    [Fact]
    public void CopyingAMapEntryReplacesOnlyTheEntryOfItsNavmesh()
    {
        var map = new NavigationMeshInfoMap(FormKey.Factory("000900:Base.esm"), FixtureMod.Release);
        map.MapInfos.Add(Entry(Navmesh));
        map.MapInfos.Add(Entry(OtherNavmesh));
        var moved = Entry(Navmesh);
        moved.Point = new P3Float(1, 2, 3);

        NavmeshRecordParts.MapEntry.CopyInto(map, moved);

        Assert.Equal([OtherNavmesh, Navmesh], map.MapInfos.Select(entry => entry.NavigationMesh.FormKey));
        Assert.False(NavmeshRecordParts.MapEntry.Differs(map.MapInfos[^1], moved));
    }

    private static NavigationMapInfo Entry(FormKey navmesh) => new()
    {
        NavigationMesh = new FormLink<INavigationMeshGetter>(navmesh),
        Parent = new NavigationMapInfoCellParent { ParentCell = new FormLink<ICellGetter>(FormKey.Factory("000A00:Base.esm")) },
    };

    private static NavigationMesh Original() => new(Navmesh, FixtureMod.Release)
    {
        EditorID = "Path",
        Data = new NavigationMeshData
        {
            Parent = new CellNavmeshParent { Parent = new FormLink<ICellGetter>(FormKey.Factory("000A00:Base.esm")) },
            Vertices = { new P3Float(0, 0, 0), new P3Float(10, 0, 0), new P3Float(0, 10, 0) },
            Triangles = { new NavmeshTriangle { Vertices = new P3Int16(0, 1, 2), EdgeLink_0_1 = -1, EdgeLink_1_2 = -1, EdgeLink_2_0 = -1 } },
        },
    };
}
