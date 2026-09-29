using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Npc;

/// <summary>NPC bodies resolved from in-memory races, skins, armour addons, NPCs and leveled lists.</summary>
public class NpcBodyResolverTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Npcs.esp");
    private const float BaseRaceBodyHeight = 140f;
    private const float VariantBodyHeight = 180f;
    private const float ShortBodyHeight = 100f;
    private const float HumanoidHeight = 128f;
    private const string BaseRaceBodyMesh = @"test\baserace_body.nif";
    private const string VariantBodyMesh = @"test\variant_body.nif";
    private const string ShortBodyMesh = @"test\short_body.nif";
    private const string HandsMesh = @"test\hands.nif";

    private static readonly FormKey BaseRace = new(Mod, 0x801);
    private static readonly FormKey VariantRace = new(Mod, 0x802);
    private static readonly FormKey ShortPlayableRace = new(Mod, 0x803);
    private static readonly FormKey Skin = new(Mod, 0x810);
    private static readonly FormKey ShortSkin = new(Mod, 0x811);
    private static readonly FormKey BaseRaceBodyAddon = new(Mod, 0x820);
    private static readonly FormKey VariantBodyAddon = new(Mod, 0x821);
    private static readonly FormKey BaseRaceHandsAddon = new(Mod, 0x822);
    private static readonly FormKey ShortBodyAddon = new(Mod, 0x823);
    private static readonly FormKey BaseRaceMale = new(Mod, 0x830);
    private static readonly FormKey BaseRaceFemale = new(Mod, 0x831);
    private static readonly FormKey VariantNpc = new(Mod, 0x832);
    private static readonly FormKey ShortNpc = new(Mod, 0x833);
    private static readonly FormKey LeveledTemplateNpc = new(Mod, 0x834);
    private static readonly FormKey OtherLeveledTemplateNpc = new(Mod, 0x835);
    private static readonly FormKey EmptyListTemplateNpc = new(Mod, 0x836);
    private static readonly FormKey Leveled = new(Mod, 0x840);
    private static readonly FormKey EmptyLeveled = new(Mod, 0x841);
    private static readonly FormKey Missing = new(Mod, 0x8FF);

    private static readonly SkyrimMod Records = CreateRecords();
    private static readonly ShapeCatalog Shapes = CreateShapes();

    [Fact]
    public void BodyMeshOfTheRaceIsSizedByTheFullExtentOfItsMeshes()
    {
        var body = SingleBody(CreateCache().GetBodies(BaseRaceMale));

        Assert.Equal(NpcSizeSource.BodyMesh, body.Source);
        Assert.Equal(BaseRaceBodyHeight, body.LocalBox.Size.Z, 3);
        Assert.Equal(TestBodies.ArmSpan, body.LocalBox.Size.X, 3);
        Assert.Equal(2 * TestBodies.TorsoHalfDepth, body.LocalBox.Size.Y, 3);
    }

    [Fact]
    public void VariantRaceUsesItsArmorRaceAddonsForTheSlotsItHasNoAddonFor()
    {
        var measurer = new SkinnedBodyMeasurer(Shapes.ReadGeometry);
        var body = SingleBody(CreateCache(measurer).GetBodies(VariantNpc));

        Assert.Equal(NpcSizeSource.BodyMesh, body.Source);
        Assert.Equal(VariantBodyHeight, body.LocalBox.Size.Z, 3);
        var measured = Assert.Single(measurer.GetMeasurements());
        Assert.Contains(HandsMesh, measured.Meshes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(VariantBodyMesh, measured.Meshes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(BaseRaceBodyMesh, measured.Meshes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FemaleWithoutAFemaleModelUsesTheMaleModel()
    {
        var body = SingleBody(CreateCache().GetBodies(BaseRaceFemale));

        Assert.Equal(NpcSizeSource.BodyMesh, body.Source);
        Assert.Equal(BaseRaceBodyHeight, body.LocalBox.Size.Z, 3);
    }

    [Fact]
    public void PlayableRaceBodyIsAtLeastHumanoidHeightBecauseTheHeadIsMissing()
    {
        var body = SingleBody(CreateCache().GetBodies(ShortNpc));

        Assert.Equal(HumanoidHeight, body.LocalBox.Size.Z, 3);
    }

    [Fact]
    public void LeveledNpcHasEveryDistinctBodyAndACombinedBox()
    {
        var bodies = CreateCache().GetBodies(LeveledTemplateNpc);

        Assert.NotNull(bodies);
        Assert.Equal(2, bodies.Bodies.Count);
        Assert.Equal(VariantBodyHeight, bodies.CombinedBox.Size.Z, 3);
        Assert.Equal(bodies.Bodies.Max(body => body.LocalBox.Size.X), bodies.CombinedBox.Size.X, 3);
    }

    [Fact]
    public void LeveledListIsResolvedOnceForEveryNpcUsingIt()
    {
        var cache = CreateCache();

        var first = cache.GetBodies(LeveledTemplateNpc);
        var second = cache.GetBodies(OtherLeveledTemplateNpc);
        var stats = cache.GetStats();

        Assert.Same(first, second);
        Assert.Equal(1, stats.ListsResolved);
        Assert.Equal(1, stats.ListsReused);
        Assert.Equal(2, stats.BasesResolved);
    }

    [Fact]
    public void BaseThatSpawnsNoNpcHasNoBody()
    {
        var cache = CreateCache();

        Assert.Null(cache.GetBodies(Missing));
        Assert.Null(cache.GetBodies(EmptyListTemplateNpc));
    }

    [Fact]
    public void PlacedNpcWithoutASpawnableBaseIsSkippedAndCounted()
    {
        var npcs = BuildIndex(
            PlaceNpc(0, BaseRaceMale, new P3Float(0, 0, 0)),
            PlaceNpc(1, Missing, new P3Float(0, 0, 0)));

        Assert.Equal(1, npcs.Count);
        Assert.Equal(0, npcs.OtherIndexOf(0));
        Assert.Equal(1, npcs.Counts.WithoutNpc);
        Assert.Equal(1, npcs.Counts.ByBodyMesh);
    }

    [Fact]
    public void PlacedNpcStandsUprightWhateverItsTilt()
    {
        var tipped = BuildIndex(PlaceNpc(0, BaseRaceMale, new P3Float(MathF.PI / 2, MathF.PI / 3, 0)));

        Assert.Equal(BaseRaceBodyHeight, tipped.WorldBoxOf(0).WorldAabb(0f).Size.Z, 3);
    }

    private static NpcBody SingleBody(NpcBodySet? bodies) => Assert.Single(Assert.IsType<NpcBodySet>(bodies).Bodies);

    private static NpcBodyCache CreateCache(SkinnedBodyMeasurer? measurer = null) =>
        new(new NpcBodyResolver(Records.ToImmutableLinkCache(), Shapes, measurer ?? new SkinnedBodyMeasurer(Shapes.ReadGeometry)));

    private static PlacedNpcIndex BuildIndex(params OtherObject[] npcs) =>
        PlacedNpcIndex.Build(OtherObjectIndex.CreateUncounted(npcs, Shapes, new ParallelOptions()), CreateCache(), new ParallelOptions());

    private static OtherObject PlaceNpc(int index, FormKey npc, P3Float rotation) => TestNpcs.Place(Mod, index, npc, Vector3.Zero, rotation);

    private static SkyrimMod CreateRecords()
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
        mod.Races.Add(TestNpcs.CreateRace(BaseRace, Skin, playable: false, armorRace: null));
        mod.Races.Add(TestNpcs.CreateRace(VariantRace, Skin, playable: false, armorRace: BaseRace));
        mod.Races.Add(TestNpcs.CreateRace(ShortPlayableRace, ShortSkin, playable: true, armorRace: null));
        mod.Armors.Add(CreateSkin(Skin, BaseRaceBodyAddon, VariantBodyAddon, BaseRaceHandsAddon));
        mod.Armors.Add(CreateSkin(ShortSkin, ShortBodyAddon));
        mod.ArmorAddons.Add(CreateAddon(BaseRaceBodyAddon, BaseRace, BipedObjectFlag.Body, BaseRaceBodyMesh));
        mod.ArmorAddons.Add(CreateAddon(VariantBodyAddon, VariantRace, BipedObjectFlag.Body, VariantBodyMesh));
        mod.ArmorAddons.Add(CreateAddon(BaseRaceHandsAddon, BaseRace, BipedObjectFlag.Hands, HandsMesh));
        mod.ArmorAddons.Add(CreateAddon(ShortBodyAddon, ShortPlayableRace, BipedObjectFlag.Body, ShortBodyMesh));
        mod.Npcs.Add(TestNpcs.CreateNpc(BaseRaceMale, BaseRace, female: false, template: null));
        mod.Npcs.Add(TestNpcs.CreateNpc(BaseRaceFemale, BaseRace, female: true, template: null));
        mod.Npcs.Add(TestNpcs.CreateNpc(VariantNpc, VariantRace, female: false, template: null));
        mod.Npcs.Add(TestNpcs.CreateNpc(ShortNpc, ShortPlayableRace, female: false, template: null));
        mod.Npcs.Add(TestNpcs.CreateNpc(LeveledTemplateNpc, BaseRace, female: false, template: Leveled));
        mod.Npcs.Add(TestNpcs.CreateNpc(OtherLeveledTemplateNpc, BaseRace, female: false, template: Leveled));
        mod.Npcs.Add(TestNpcs.CreateNpc(EmptyListTemplateNpc, BaseRace, female: false, template: EmptyLeveled));
        mod.LeveledNpcs.Add(TestNpcs.CreateLeveledList(Leveled, BaseRaceMale, BaseRaceFemale, VariantNpc, Missing));
        mod.LeveledNpcs.Add(TestNpcs.CreateLeveledList(EmptyLeveled));
        return mod;
    }

    private static ShapeCatalog CreateShapes()
    {
        var dataPath = Path.Combine(AppContext.BaseDirectory, "NpcBodyData");
        TestShapes.WriteMesh(dataPath, BaseRaceBodyMesh, TestBodies.TPose(BaseRaceBodyHeight));
        TestShapes.WriteMesh(dataPath, VariantBodyMesh, TestBodies.TPose(VariantBodyHeight));
        TestShapes.WriteMesh(dataPath, ShortBodyMesh, TestBodies.TPose(ShortBodyHeight));
        TestShapes.WriteMesh(dataPath, HandsMesh, TestMeshes.BoxTriangles(new Box(new Vector3(-5, -5, 60), new Vector3(5, 5, 70))));

        var problems = new AssetProblemLog();
        var meshFiles = new MeshFileSource(dataPath, GameRelease.SkyrimSE, [Mod], problems);
        return new ShapeCatalog(new BaseFactsReader(Records.ToImmutableLinkCache()), meshFiles, problems);
    }

    private static Armor CreateSkin(FormKey formKey, params FormKey[] addons)
    {
        var armor = new Armor(formKey, SkyrimRelease.SkyrimSE);
        foreach (var addon in addons) armor.Armature.Add(new FormLink<IArmorAddonGetter>(addon));
        return armor;
    }

    /// <summary>Only a male world model, as creature skins often have.</summary>
    private static ArmorAddon CreateAddon(FormKey formKey, FormKey race, BipedObjectFlag slots, string maleModel) =>
        new(formKey, SkyrimRelease.SkyrimSE)
        {
            Race = new FormLinkNullable<IRaceGetter>(race),
            BodyTemplate = new BodyTemplate { FirstPersonFlags = slots },
            WorldModel = new GenderedItem<Model?>(new Model { File = maleModel }, null),
        };
}
