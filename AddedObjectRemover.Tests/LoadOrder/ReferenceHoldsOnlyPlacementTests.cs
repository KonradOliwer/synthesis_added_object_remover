using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

public class ReferenceHoldsOnlyPlacementTests : IDisposable
{
    private const SkyrimRelease Release = SkyrimRelease.SkyrimSE;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("References.esp");
    private static readonly FormKey Other = new(Mod, 0xA00);

    private readonly string _folder = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>Each row adds one group of the fields a plain scenery reference never holds.</summary>
    public static TheoryData<string> FieldGroups => [.. FieldGroupsByName.Keys];

    private static readonly Dictionary<string, Action<PlacedObject>> FieldGroupsByName = new()
    {
        ["scripts"] = record => record.VirtualMachineAdapter = new VirtualMachineAdapter { Scripts = [new ScriptEntry { Name = "SomeScript" }] },
        ["linked references"] = record => record.LinkedReferences.Add(new LinkedReferences { Reference = new FormLink<IPlacedGetter>(Other) }),
        ["enable parent"] = record => record.EnableParent = new EnableParent { Reference = new FormLink<IPlacedGetter>(Other) },
        ["activate parents"] = record => record.ActivateParents = new ActivateParents { Parents = [new ActivateParent { Reference = new FormLink<IPlacedGetter>(Other) }] },
        ["attached and multibound references"] = record => record.MultiBoundReference.SetTo(Other),
        ["location and encounter zone"] = record => record.EncounterZone.SetTo(Other),
        ["location ref types"] = record => record.LocationRefTypes = [new FormLink<ILocationReferenceTypeGetter>(Other)],
        ["owner"] = record => record.Owner.SetTo(Other),
        ["lock"] = record => record.Lock = new LockData { Level = LockLevel.Novice },
        ["teleport destination"] = record => record.TeleportDestination = new TeleportDestination { Door = new FormLink<IPlacedObjectGetter>(Other) },
        ["map marker"] = record => record.MapMarker = new MapMarker { Name = "Somewhere" },
        ["patrol"] = record => record.Patrol = new Patrol { IdleTime = 1f },
        ["room and portal data"] = record => record.IsMultiBoundPrimitive = true,
        ["primitive"] = record => record.Primitive = new PlacedPrimitive { Bounds = new P3Float(1, 1, 1) },
        ["water data"] = record => record.LitWater.Add(new FormLink<IPlacedObjectGetter>(Other)),
        ["radius"] = record => record.Radius = 64f,
        ["emittance"] = record => record.Emittance.SetTo(Other),
        ["light data"] = record => record.LightData = new LightData { FovOffset = 90f },
        ["distant LOD data"] = record => record.DistantLodData = new byte[12],
        ["collision layer"] = record => record.CollisionLayer = 1,
        ["item count"] = record => record.ItemCount = 3,
    };

    [Fact]
    public void AReferenceWithBasePlacementScaleAndEditorIdOnlyHoldsOnlyItsPlacement()
    {
        var mod = new SkyrimMod(Mod, Release);
        AddPlain(mod);

        Assert.True(mod.Cells.Records.Single().SubBlocks.Single().Cells.Single().Temporary.Single().HoldsOnlyPlacement());
    }

    [Fact]
    public void AReferenceReadFromAPluginFileHoldsOnlyItsPlacementWhenItStoresNothingElse()
    {
        var mod = new SkyrimMod(Mod, Release);
        AddPlain(mod);

        using var read = WriteAndOpen(mod);

        Assert.True(TheReferenceOf(read).HoldsOnlyPlacement());
    }

    [Theory]
    [MemberData(nameof(FieldGroups))]
    public void AReferenceHoldingAnyOtherFieldDoesNotHoldOnlyItsPlacement(string group)
    {
        var mod = new SkyrimMod(Mod, Release);
        var record = AddPlain(mod);
        FieldGroupsByName[group](record);

        using var read = WriteAndOpen(mod);

        Assert.False(record.HoldsOnlyPlacement());
        Assert.False(TheReferenceOf(read).HoldsOnlyPlacement());
    }

    [Fact]
    public void ThePersistentAndInitiallyDisabledFlagsAreNoFieldsTheTestLooksAt()
    {
        var mod = new SkyrimMod(Mod, Release);
        var record = AddPlain(mod);
        record.MajorRecordFlagsRaw |= (int)(PlacedObject.DefaultMajorFlag.Persistent | PlacedObject.DefaultMajorFlag.InitiallyDisabled);

        Assert.True(record.HoldsOnlyPlacement());
    }

    [Fact]
    public void TheVisibleWhenDistantFlagIsReadFromTheReferencesFlags()
    {
        var mod = new SkyrimMod(Mod, Release);
        var record = AddPlain(mod);
        Assert.False(record.IsVisibleWhenDistant());

        record.MajorRecordFlagsRaw |= (int)PlacedObject.StaticMajorFlag.VisibleWhenDistant;

        Assert.True(record.IsVisibleWhenDistant());
    }

    private static PlacedObject AddPlain(SkyrimMod mod)
    {
        var rock = mod.Statics.AddNew("Rock");
        var cell = new Cell(mod.GetNextFormKey(), Release) { EditorID = "SomeCell" };
        var record = new PlacedObject(mod.GetNextFormKey(), Release)
        {
            EditorID = "RockRef",
            Base = new FormLinkNullable<IPlaceableObjectGetter>(rock.FormKey),
            Placement = new Placement { Position = new P3Float(1, 2, 3), Rotation = new P3Float(0, 0, 1) },
            Scale = 1.5f,
        };
        cell.Temporary.Add(record);
        mod.Cells.Records.Add(new CellBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock, SubBlocks = [new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = [cell] }] });
        return record;
    }

    private ISkyrimModDisposableGetter WriteAndOpen(SkyrimMod mod)
    {
        var path = Path.Combine(_folder, mod.ModKey.FileName);
        mod.WriteToBinary(path);
        return SkyrimMod.CreateFromBinaryOverlay(path, Release);
    }

    private static IPlacedGetter TheReferenceOf(ISkyrimModGetter mod) =>
        mod.Cells.Records.Single().SubBlocks.Single().Cells.Single().Temporary.Single();
}
