using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

public class BaseFactsReaderTests
{
    private const SkyrimRelease Release = SkyrimRelease.SkyrimSE;

    private readonly SkyrimMod _mod = new(ModKey.FromNameAndExtension("Bases.esp"), Release);

    private BaseFacts Read<TGetter>(IMajorRecordGetter record) where TGetter : class, IMajorRecordGetter =>
        new BaseFactsReader(_mod.ToImmutableLinkCache()).Of(new BaseKey(record.FormKey.ToRecordKey(), LinkKindOf<TGetter>()));

    private static BaseLinkKind LinkKindOf<TGetter>() =>
        typeof(TGetter) == typeof(INpcGetter) ? BaseLinkKind.Npc : BaseLinkKind.PlaceableObject;

    private static Model ModelOf(string path) => new() { File = path };

    [Fact]
    public void AnUnresolvedBaseIsUnresolved()
    {
        var missing = new FormKey(_mod.ModKey, 0xABC).ToRecordKey();
        var facts = new BaseFactsReader(_mod.ToImmutableLinkCache()).Of(new BaseKey(missing, BaseLinkKind.PlaceableObject));

        Assert.Equal(BaseFacts.Unresolved(missing), facts);
        Assert.False(facts.Resolved);
    }

    [Fact]
    public void AStaticGivesKindTypeNameEditorIdModelAndBounds()
    {
        var record = _mod.Statics.AddNew("Sample");
        record.Model = ModelOf(@"meshes\sample.nif");
        record.ObjectBounds = new ObjectBounds { First = new P3Int16(-1, -2, -3), Second = new P3Int16(4, 5, 6) };

        var facts = Read<IStaticGetter>(record);

        Assert.True(facts.Resolved);
        Assert.Equal(BaseRecordKind.Static, facts.Kind);
        Assert.Equal("Static", facts.RecordTypeName);
        Assert.Equal("Sample", facts.EditorId);
        Assert.Equal(@"meshes\sample.nif", facts.ModelPath);
        Assert.Equal(Box.FromCorners(new(-1, -2, -3), new(4, 5, 6)), facts.ObjectBounds);
        Assert.Null(facts.MarkerFlag);
        Assert.Empty(facts.ScriptNames);
    }

    [Fact]
    public void ABlankModelPathIsNoModel()
    {
        var record = _mod.Statics.AddNew("Blank");
        record.Model = ModelOf("   ");

        Assert.Null(Read<IStaticGetter>(record).ModelPath);
    }

    [Fact]
    public void AStaticWithoutAModelHasNoModelPath()
    {
        Assert.Null(Read<IStaticGetter>(_mod.Statics.AddNew("Bare")).ModelPath);
    }

    [Fact]
    public void TheMarkerFlagMapsPerBaseType()
    {
        var xMarker = _mod.Statics.AddNew();
        xMarker.MajorFlags = Static.MajorFlag.IsMarker;
        var furniture = _mod.Furniture.AddNew();
        furniture.MajorFlags = Furniture.MajorFlag.IsMarker;
        var activator = _mod.Activators.AddNew();
        activator.MajorFlags = Mutagen.Bethesda.Skyrim.Activator.MajorFlag.IsMarker;
        var door = _mod.Doors.AddNew();
        door.MajorFlags = Door.MajorFlag.IsMarker;

        Assert.Equal(MarkerFlagKind.XMarker, Read<IStaticGetter>(xMarker).MarkerFlag);
        Assert.Equal(MarkerFlagKind.FurnitureMarker, Read<IFurnitureGetter>(furniture).MarkerFlag);
        Assert.Equal(MarkerFlagKind.OtherMarker, Read<IActivatorGetter>(activator).MarkerFlag);
        Assert.Equal(MarkerFlagKind.DoorMarker, Read<IDoorGetter>(door).MarkerFlag);
    }

    [Fact]
    public void ARecordWithoutTheMarkerBitHasNoMarkerFlag()
    {
        Assert.Null(Read<IDoorGetter>(_mod.Doors.AddNew()).MarkerFlag);
    }

    [Fact]
    public void ATypeOutsideTheFourMarkerTypesNeverHasAMarkerFlagEvenWithTheSameBit()
    {
        var container = _mod.Containers.AddNew();
        container.MajorRecordFlagsRaw = (int)Static.MajorFlag.IsMarker;

        var facts = Read<IContainerGetter>(container);

        Assert.Null(facts.MarkerFlag);
        Assert.Equal(BaseRecordKind.Other, facts.Kind);
    }

    [Fact]
    public void KindsFollowTheRecordType()
    {
        Assert.Equal(BaseRecordKind.Npc, Read<INpcGetter>(_mod.Npcs.AddNew()).Kind);
        Assert.Equal(BaseRecordKind.Light, Read<ILightGetter>(_mod.Lights.AddNew()).Kind);
        Assert.Equal(BaseRecordKind.SoundMarker, Read<ISoundMarkerGetter>(_mod.SoundMarkers.AddNew()).Kind);
        Assert.Equal(BaseRecordKind.AcousticSpace, Read<IAcousticSpaceGetter>(_mod.AcousticSpaces.AddNew()).Kind);
        Assert.Equal(BaseRecordKind.TextureSet, Read<ITextureSetGetter>(_mod.TextureSets.AddNew()).Kind);
        Assert.Equal(BaseRecordKind.IdleMarker, Read<IIdleMarkerGetter>(_mod.IdleMarkers.AddNew()).Kind);
        Assert.Equal(BaseRecordKind.Furniture, Read<IFurnitureGetter>(_mod.Furniture.AddNew()).Kind);
        Assert.Equal(BaseRecordKind.Activator, Read<IActivatorGetter>(_mod.Activators.AddNew()).Kind);
        Assert.Equal(BaseRecordKind.Door, Read<IDoorGetter>(_mod.Doors.AddNew()).Kind);
        Assert.Equal(BaseRecordKind.Other, Read<IContainerGetter>(_mod.Containers.AddNew()).Kind);
    }

    [Fact]
    public void AnActivatorGivesTheNamesOfItsScripts()
    {
        var activator = _mod.Activators.AddNew();
        activator.VirtualMachineAdapter = new VirtualMachineAdapter();
        activator.VirtualMachineAdapter.Scripts.Add(new ScriptEntry { Name = "FirstScript" });
        activator.VirtualMachineAdapter.Scripts.Add(new ScriptEntry { Name = "SecondScript" });

        Assert.Equal(new[] { "FirstScript", "SecondScript" }, Read<IActivatorGetter>(activator).ScriptNames.ToArray());
    }

    [Fact]
    public void AnActivatorWithoutScriptsHasNoScriptNames()
    {
        Assert.Empty(Read<IActivatorGetter>(_mod.Activators.AddNew()).ScriptNames);
    }

    [Theory]
    [InlineData(256, 256f)]
    [InlineData(0, null)]
    public void ALightGivesItsRadiusOnlyWhenAboveZero(int radius, float? expected)
    {
        var light = _mod.Lights.AddNew();
        light.Radius = (uint)radius;

        Assert.Equal(expected, Read<ILightGetter>(light).LightRadius);
    }

    [Fact]
    public void ASoundMarkerGivesTheMaximumDistanceOfItsOutputModelAttenuation()
    {
        var output = _mod.SoundOutputModels.AddNew();
        output.Attenuation = new SoundOutputAttenuation { MaxDistance = 1500 };
        var descriptor = _mod.SoundDescriptors.AddNew();
        descriptor.OutputModel.SetTo(output);
        var marker = _mod.SoundMarkers.AddNew();
        marker.SoundDescriptor.SetTo(descriptor);

        Assert.Equal(1500f, Read<ISoundMarkerGetter>(marker).SoundMaxDistance);
    }

    [Fact]
    public void ASoundMarkerWithoutAResolvableDescriptorHasNoMaximumDistance()
    {
        var marker = _mod.SoundMarkers.AddNew();
        marker.SoundDescriptor.SetTo(new FormKey(_mod.ModKey, 0xDEAD));

        Assert.Null(Read<ISoundMarkerGetter>(marker).SoundMaxDistance);
    }

    [Fact]
    public void ASoundMarkerWhoseOutputModelIsMissingHasNoMaximumDistance()
    {
        var descriptor = _mod.SoundDescriptors.AddNew();
        descriptor.OutputModel.SetTo(new FormKey(_mod.ModKey, 0xDEAD));
        var marker = _mod.SoundMarkers.AddNew();
        marker.SoundDescriptor.SetTo(descriptor);

        Assert.Null(Read<ISoundMarkerGetter>(marker).SoundMaxDistance);
    }

    [Fact]
    public void RepeatedAndConcurrentReadsOfOneBaseShareOneResult()
    {
        var record = _mod.Statics.AddNew("Shared");
        var reader = new BaseFactsReader(_mod.ToImmutableLinkCache());
        var baseKey = new BaseKey(record.FormKey.ToRecordKey(), BaseLinkKind.PlaceableObject);
        var results = new BaseFacts[32];

        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => results[i] = reader.Of(baseKey));

        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Same(results[0], reader.Of(baseKey));
    }
}
