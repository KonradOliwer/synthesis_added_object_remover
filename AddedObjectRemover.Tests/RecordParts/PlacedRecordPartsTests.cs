using AddedObjectRemover.Tests.EndToEnd;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.RecordParts;

/// <summary>Each part of a placed record is seen alone, the rest holds every unlisted field, and a copy writes exactly what was compared.</summary>
public class PlacedRecordPartsTests
{
    private const float PositionTolerance = 0.01f;
    private const float RotationTolerance = 0.0001f;
    private const float ScaleTolerance = 0.0001f;
    private const ushort OlderFormVersion = 43;
    private const ushort NewerFormVersion = 44;

    private static readonly FormKey Record = FormKey.Factory("000800:Base.esm");
    private static readonly PlacedRecordParts Table = new(new PlacedPartTolerances(PositionTolerance, RotationTolerance, ScaleTolerance));

    /// <summary>One change per case, with the only part it shows in.</summary>
    public static TheoryData<string, PlacedParts> Changes() => new()
    {
        { "moved", PlacedParts.Position },
        { "turned", PlacedParts.Rotation },
        { "scaled", PlacedParts.Scale },
        { "disabled", PlacedParts.Presence },
        { "deleted", PlacedParts.Presence },
        { "enable parent", PlacedParts.Presence },
        { "door link", PlacedParts.NavDoorLink },
        { "base", PlacedParts.BaseAndData },
        { "charge (on no list)", PlacedParts.BaseAndData },
        { "light data (on no list)", PlacedParts.BaseAndData },
        { "visible when distant flag", PlacedParts.BaseAndData },
        { "editor id (excluded)", PlacedParts.None },
        { "form version (excluded)", PlacedParts.None },
        { "persistent flag (excluded)", PlacedParts.None },
        { "moved within the tolerance", PlacedParts.None },
        { "turned a full circle", PlacedParts.None },
        { "scale stored as 1", PlacedParts.None },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void AChangeShowsOnlyInItsOwnPart(string change, PlacedParts expected) =>
        Assert.Equal(expected, ChangedParts(Original(), Changed(change)));

    [Theory]
    [MemberData(nameof(Changes))]
    public void CopyingAPartWritesItAndNothingElse(string change, PlacedParts part)
    {
        if (part == PlacedParts.None) return;
        var source = Changed(change);
        var target = Original();

        Compare(part).CopyInto(target, source);

        Assert.False(Compare(part).Differs(target, source));
        Assert.Equal(part, ChangedParts(Original(), target));
    }

    [Fact]
    public void ThePartsAreListedOnceWithTheRestLast() =>
        Assert.Equal(
            [PlacedParts.Position, PlacedParts.Rotation, PlacedParts.Scale, PlacedParts.Presence, PlacedParts.NavDoorLink, PlacedParts.BaseAndData],
            Table.Parts.Select(entry => entry.Part));

    [Fact]
    public void AnActorsUnlistedFieldFallsIntoTheRest()
    {
        var actor = new PlacedNpc(Record, FixtureMod.Release) { Placement = new Placement() };
        var changed = (PlacedNpc)actor.DeepCopy();
        changed.LevelModifier = Level.Hard;

        Assert.Equal(PlacedParts.BaseAndData, ChangedParts(actor, changed));
        Compare(PlacedParts.BaseAndData).CopyInto(actor, changed);
        Assert.Equal(Level.Hard, actor.LevelModifier);
    }

    [Fact]
    public void AProjectilesUnlistedFieldFallsIntoTheRest()
    {
        var arrow = new PlacedArrow(Record, FixtureMod.Release) { Placement = new Placement() };
        var changed = (PlacedArrow)arrow.DeepCopy();
        changed.FavorCost = 3f;

        Assert.Equal(PlacedParts.BaseAndData, ChangedParts(arrow, changed));
        Compare(PlacedParts.BaseAndData).CopyInto(arrow, changed);
        Assert.Equal(3f, arrow.FavorCost);
    }

    [Fact]
    public void AHazardsUnlistedFieldFallsIntoTheRest()
    {
        var hazard = new PlacedHazard(Record, FixtureMod.Release) { Placement = new Placement() };
        var changed = (PlacedHazard)hazard.DeepCopy();
        changed.FavorCost = 3f;

        Assert.Equal(PlacedParts.None, ChangedParts(hazard, (PlacedHazard)hazard.DeepCopy()));
        Assert.Equal(PlacedParts.BaseAndData, ChangedParts(hazard, changed));
        Compare(PlacedParts.BaseAndData).CopyInto(hazard, changed);
        Assert.Equal(3f, hazard.FavorCost);
    }

    [Fact]
    public void RawBytesAreWrittenOnlyIntoTheSameFormVersion()
    {
        var source = Original();
        source.FormVersion = OlderFormVersion;
        source.XWCN = new byte[] { 1 };
        var sameFormat = Original();
        sameFormat.FormVersion = OlderFormVersion;
        var otherFormat = Original();
        otherFormat.FormVersion = NewerFormVersion;
        var rest = Compare(PlacedParts.BaseAndData);

        Assert.True(RawByteFormat.AllowsCopy(rest, source, sameFormat));
        Assert.False(RawByteFormat.AllowsCopy(rest, source, otherFormat));
        Assert.True(RawByteFormat.AllowsCopy(Compare(PlacedParts.Position), source, otherFormat));
    }

    [Fact]
    public void AVersionWithoutRawBytesIsWrittenIntoAnyFormVersion()
    {
        var source = Original();
        source.FormVersion = OlderFormVersion;
        var target = Original();
        target.FormVersion = NewerFormVersion;

        Assert.True(RawByteFormat.AllowsCopy(Compare(PlacedParts.BaseAndData), source, target));
    }

    [Fact]
    public void OnlyTheIdentityAndHeaderStampsAndThePersistentFlagAreExcluded()
    {
        Assert.Equal(["FormKey", "EditorID", "VersionControl", "FormVersion", "Version2"], PlacedRecordParts.ExcludedFields);
        Assert.Equal((int)(PlacedObject.DefaultMajorFlag.Persistent) | (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.Compressed, PlacedRecordParts.ExcludedFlagBits);
    }

    private static RecordPart<IPlacedGetter, IPlaced> Compare(PlacedParts part) => Table.Parts.Single(entry => entry.Part == part).Compare;

    private static PlacedParts ChangedParts(IPlacedGetter one, IPlacedGetter other) =>
        Table.Parts.Where(entry => entry.Compare.Differs(one, other)).Aggregate(PlacedParts.None, (parts, entry) => parts | entry.Part);

    private static PlacedObject Original()
    {
        var record = new PlacedObject(Record, FixtureMod.Release)
        {
            EditorID = "Rock",
            Placement = new Placement { Position = new P3Float(100, 200, 300), Rotation = new P3Float(0.1f, 0.2f, 0.3f) },
        };
        record.Base.SetTo(FormKey.Factory("000900:Base.esm"));
        return record;
    }

    private static EnableParent EnableParent()
    {
        var parent = new EnableParent();
        parent.Reference.SetTo(FormKey.Factory("000A00:Base.esm"));
        return parent;
    }

    private static PlacedObject Changed(string change)
    {
        var record = Original();
        switch (change)
        {
            case "moved": record.Placement!.Position = new P3Float(110, 200, 300); break;
            case "turned": record.Placement!.Rotation = new P3Float(0.1f, 0.2f, 1.3f); break;
            case "scaled": record.Scale = 2f; break;
            case "disabled": record.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled; break;
            case "deleted": record.IsDeleted = true; break;
            case "enable parent": record.EnableParent = EnableParent(); break;
            case "door link": record.NavigationDoorLink = new NavigationDoorLink { NavMesh = new FormLink<INavigationMeshGetter>(FormKey.Factory("000B00:Base.esm")), TeleportMarkerTriangle = 4 }; break;
            case "base": record.Base.SetTo(FormKey.Factory("000901:Base.esm")); break;
            case "charge (on no list)": record.Charge = 5f; break;
            case "light data (on no list)": record.LightData = new LightData { FadeOffset = 2f }; break;
            case "visible when distant flag": record.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.VisibleWhenDistant; break;
            case "editor id (excluded)": record.EditorID = "OtherRock"; break;
            case "form version (excluded)": record.FormVersion = NewerFormVersion; break;
            case "persistent flag (excluded)": record.MajorRecordFlagsRaw |= (int)PlacedObject.DefaultMajorFlag.Persistent; break;
            case "moved within the tolerance": record.Placement!.Position = new P3Float(100.005f, 200, 300); break;
            case "turned a full circle": record.Placement!.Rotation = new P3Float(0.1f, 0.2f, 0.3f + 2 * MathF.PI); break;
            case "scale stored as 1": record.Scale = 1f; break;
            default: throw new ArgumentException(change, nameof(change));
        }
        return record;
    }
}
