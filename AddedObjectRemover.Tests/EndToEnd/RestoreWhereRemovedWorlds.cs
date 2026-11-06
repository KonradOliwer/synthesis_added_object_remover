using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Small worlds in which the cleaned mod's objects are removed from a whole area, so the step that restores what the cleaned mod
/// changed there has something to act on. The base game is Skyrim.esm, the cleaned mod is the fixture's target, and CityMod.esp is
/// a later plugin whose pads stand beside the cleaned mod's crates (which removes those crates) and which changes some of what the
/// cleaned mod changed.
/// </summary>
internal static class RestoreWhereRemovedWorlds
{
    public const string BaseGamePlugin = "Skyrim.esm";
    public const string CityPlugin = "CityMod.esp";

    /// <summary>A plugin that has the cleaned mod as a master.</summary>
    public const string PatchOfTheCleanedModPlugin = "VillagePatch.esp";

    /// <summary>A plugin that has <see cref="PatchOfTheCleanedModPlugin"/> as a master, so it needs the cleaned mod only through it.</summary>
    public const string AddOnOfThePatchPlugin = "VillageAddOn.esp";

    public const float BaseFogNear = 10f;
    public const float CleanedModFogNear = 20f;
    public const float CleanedModTurn = 1f;
    public const float CleanedModScale = 2f;
    public const float CityScale = 3f;

    /// <summary>The cleaned mod's height of the land in the terrain world, in height steps; the base game's is <see cref="BaseLandSteps"/>.</summary>
    public const float CleanedModLandSteps = 24f;

    public const float BaseLandSteps = 16f;
    public const float UnitsPerLandStep = TerrainGrid.UnitsPerHeightStep;

    /// <summary>Where the other mods' objects stand in the village, along a line.</summary>
    public static readonly Vector3 RockAt = At(2000, 0);
    public static readonly Vector3 StumpAt = At(2000, 1000);
    public static readonly Vector3 MoverAt = At(3000, 0);
    public static readonly Vector3 MoverMovedTo = At(3000, 600);
    public static readonly Vector3 LampAt = At(4000, 0);
    public static readonly Vector3 StatueAt = At(5000, 0);
    public static readonly Vector3 TableAt = At(6000, 0);

    private const float PadDistance = 75f;

    /// <summary>How far the table stands from the rock's place, so that it reaches into the space the rock fills.</summary>
    private const float TableOffset = 60f;

    /// <summary>A tiny object 5 units from the side of a crate: too close to it, yet far too small to count against clearing the area.</summary>
    private const float PebbleDistance = 60f;

    private const int InitiallyDisabled = (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;
    private const int SiteRows = 3;
    private const int SiteColumns = 5;
    private const int MiddleRow = 1;
    private const int Side = TerrainGrid.VerticesPerSide;

    /// <summary>
    /// The interior, where the cleaned mod has two crates of its own, each with a city pad beside it, so all of its objects there are
    /// removed. Beside them it changed, among other mods' objects: a rock (disabled), a stump (deleted), a crate (moved), a lamp
    /// (turned and resized, then resized again by the city mod), a statue (disabled, with a tiny object of the cleaned mod that
    /// stays beside its place), a table (resized, with a city lantern resting on its top) and the cell's music, location and lighting
    /// (music changed again by the city mod). Expected to go back: all but the statue and the table, the lamp's size and the cell's
    /// music.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateVillage(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var (baseGame, target, city, _) = CreateMods();
        ChangeTheCellSettings(baseGame, target, city);
        var crate = baseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel);
        var pad = city.AddModeledStatic("C_PadBase", FixtureMeshes.PadModel);
        var own = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        foreach (var (name, at) in new[] { ("T_Crate1", At(0, 0)), ("T_Crate2", At(0, 1000)) })
        {
            target.Interior.Place(name, own, at);
            city.Interior.Place("C_Pad" + name, pad, at + new Vector3(PadDistance, 0, 0));
        }

        target.Interior.PlaceCopyOf(baseGame.Interior.Place("B_Rock", crate, RockAt)).MajorRecordFlagsRaw |= InitiallyDisabled;
        DeleteInTarget(target, baseGame.Interior.Place("B_Stump", crate, StumpAt));
        MoveCopy(target.Interior.PlaceCopyOf(baseGame.Interior.Place("B_Mover", crate, MoverAt)), MoverMovedTo);
        TurnAndResizeInBothMods(baseGame.Interior.Place("B_Lamp", crate, LampAt), target, city);
        target.Interior.PlaceCopyOf(baseGame.Interior.Place("B_Statue", crate, StatueAt)).MajorRecordFlagsRaw |= InitiallyDisabled;
        target.Interior.Place("T_Pebble", target.AddBoundsOnlyStatic("T_PebbleBase", new Box(new Vector3(-5, -5, 0), new Vector3(5, 5, 10))), StatueAt + new Vector3(PebbleDistance, 0, 0));
        target.Interior.PlaceCopyOf(baseGame.Interior.Place("B_Table", crate, TableAt)).Scale = CleanedModScale;
        city.Interior.Place("C_Lantern", pad, TableAt + new Vector3(0, 0, FixtureMeshes.CrateSize * CleanedModScale));
        return [baseGame.Mod, target.Mod, city.Mod];
    }

    /// <summary>
    /// The exterior cell at grid (0, 0), where the cleaned mod raised the land by 64 units and has two crates of its own: one in the
    /// middle of the cell, which has a city pad beside it and is removed, and one in its north-east quarter, which stays. With a
    /// search radius of 1500 units, the other three quarters see only the removed crate and count as cleared.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateTerrain(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var (baseGame, target, city, _) = CreateLandWithTwoCrates(BaseLandSteps, CleanedModLandSteps);
        return [baseGame.Mod, target.Mod, city.Mod];
    }

    /// <summary>A plugin the run trusts in full, which places a shrine on the ground the cleaned mod lowered.</summary>
    public const string ShrinePlugin = "ShrineMod.esp";

    /// <summary>Where the shrine stands: the centre of the south-west quarter of the exterior cell (0, 0), on the lowered ground.</summary>
    public static readonly Vector3 ShrineAt = new(ExteriorGrid.CellSize / 4, ExteriorGrid.CellSize / 4, BaseLandSteps * UnitsPerLandStep);

    /// <summary>
    /// <see cref="CreateTerrain"/> with the land lowered instead: the base game's is <see cref="CleanedModLandSteps"/> high and the cleaned
    /// mod's <see cref="BaseLandSteps"/>. <see cref="ShrinePlugin"/> loads after the city mod and places a shrine on the lowered ground in
    /// the south-west quarter, which counts as cleared. The cleaned mod changed no placed record of another plugin.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateLoweredTerrainWithAShrine(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var (baseGame, target, city, spaces) = CreateLandWithTwoCrates(CleanedModLandSteps, BaseLandSteps);
        var shrineMod = new FixtureMod(ShrinePlugin, baseGame);
        spaces.AddOverridesTo(shrineMod);
        shrineMod.Exterior.Place("S_Shrine", shrineMod.AddModeledStatic("S_ShrineBase", FixtureMeshes.CrateModel), ShrineAt);
        return [baseGame.Mod, target.Mod, city.Mod, shrineMod.Mod];
    }

    /// <summary>
    /// The base game's and the cleaned mod's flat land of the exterior cell (0, 0), with the cleaned mod's crate in the middle of the
    /// cell beside a city pad and its crate in the north-east quarter, both on the cleaned mod's land.
    /// </summary>
    private static (FixtureMod BaseGame, FixtureMod Target, FixtureMod City, FixtureSpaces Spaces) CreateLandWithTwoCrates(float baseSteps, float cleanedModSteps)
    {
        var mods = CreateMods();
        var (baseGame, target, city, _) = mods;
        var land = baseGame.Exterior.Record.Landscape!.FormKey;
        baseGame.Exterior.Record.Landscape = FlatLand(land, baseSteps);
        target.Exterior.Record.Landscape = FlatLand(land, cleanedModSteps);
        var ground = cleanedModSteps * UnitsPerLandStep;
        var own = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        var middle = new Vector3(ExteriorGrid.CellSize / 2, ExteriorGrid.CellSize / 2, ground);
        target.Exterior.Place("T_Middle", own, middle);
        city.Exterior.Place("C_Pad", city.AddModeledStatic("C_PadBase", FixtureMeshes.PadModel), middle + new Vector3(PadDistance, 0, 0));
        target.Exterior.Place("T_Standing", own, new Vector3(ExteriorGrid.CellSize * 3 / 4, ExteriorGrid.CellSize * 3 / 4, ground));
        return mods;
    }

    /// <summary>
    /// The interior, where the cleaned mod has a crate with a city pad beside it, so its only object is removed, and disabled a rock of
    /// the base game; a patch that requires the cleaned mod places a table where the rock stood. The cleaned mod moved and replaced
    /// nothing, so no old place is judged. Expected: the rock stays disabled, too close to the table.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateVillageWithAPatchOfTheCleanedMod(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var (baseGame, target, city, spaces) = CreateMods();
        var patch = new FixtureMod(PatchOfTheCleanedModPlugin, baseGame, target);
        spaces.AddOverridesTo(patch);
        var crate = baseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel);
        target.Interior.Place("T_Crate1", target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel), At(0, 0));
        city.Interior.Place("C_PadT_Crate1", city.AddModeledStatic("C_PadBase", FixtureMeshes.PadModel), At(PadDistance, 0));
        target.Interior.PlaceCopyOf(baseGame.Interior.Place("B_Rock", crate, RockAt)).MajorRecordFlagsRaw |= InitiallyDisabled;
        patch.Interior.Place("P_Table", patch.AddModeledStatic("P_TableBase", FixtureMeshes.CrateModel), RockAt);
        return [baseGame.Mod, target.Mod, city.Mod, patch.Mod];
    }

    /// <summary>
    /// The interior, where the cleaned mod has a crate with a city pad beside it, so its only object is removed, and disabled a rock of
    /// the base game; an add-on of the patch of the cleaned mod, which needs the cleaned mod only through that patch, places a table
    /// beside the rock's place. Expected: the rock stays disabled, too close to the table.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateVillageWithATableOfAnotherModWhereARockStood(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var (baseGame, target, city, spaces) = CreateMods();
        var patch = new FixtureMod(PatchOfTheCleanedModPlugin, baseGame, target);
        var addOn = new FixtureMod(AddOnOfThePatchPlugin, baseGame, patch);
        spaces.AddOverridesTo(patch);
        spaces.AddOverridesTo(addOn);
        var crate = baseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel);
        target.Interior.Place("T_Crate1", target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel), At(0, 0));
        city.Interior.Place("C_PadT_Crate1", city.AddModeledStatic("C_PadBase", FixtureMeshes.PadModel), At(PadDistance, 0));
        target.Interior.PlaceCopyOf(baseGame.Interior.Place("B_Rock", crate, RockAt)).MajorRecordFlagsRaw |= InitiallyDisabled;
        addOn.Interior.Place("A_Table", addOn.AddModeledStatic("A_TableBase", FixtureMeshes.CrateModel), RockAt + new Vector3(TableOffset, 0, 0));
        return [baseGame.Mod, target.Mod, city.Mod, patch.Mod, addOn.Mod];
    }

    /// <summary>Where the base game's piece the cleaned mod disabled stands, beside the cleaned mod's site.</summary>
    public static readonly Vector3 HiddenAt = At(100, 600);

    /// <summary>Where the base game's piece the cleaned mod moved stood, and where it moved it.</summary>
    public static readonly Vector3 MovedFrom = At(100, -600);
    public static readonly Vector3 MovedTo = At(100, -1000);

    /// <summary>
    /// The interior, where the cleaned mod built a site of its own: crates side by side in three rows of five, which touch each other,
    /// with a mist (a mesh drawn only with an effect shader, three crates wide) in place of the second and the fourth crate of the
    /// middle row, so each mist covers the side of the other that faces it. A city pad stands beside the first crate of the first row,
    /// so that crate is too close, and the crates touching it go with it; the mists then stand among removed crates and each other. The
    /// cleaned mod also disabled a base-game crate and moved another out of the site.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateSiteWithMists(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        FixtureMeshes.WriteMist(dataFolder);
        var (baseGame, target, city, _) = CreateMods();
        var (block, mist) = (target.AddModeledStatic("T_BlockBase", FixtureMeshes.CrateModel), target.AddModeledStatic("T_MistBase", FixtureMeshes.MistModel));
        for (var row = 0; row < SiteRows; row++)
        {
            for (var column = 0; column < SiteColumns; column++)
            {
                var at = At((column - 1) * FixtureMeshes.CrateSize, (row - 1) * FixtureMeshes.CrateSize);
                var isMist = row == MiddleRow && column % 2 == 1;
                target.Interior.Place($"T_{(isMist ? "Mist" : "Block")}{row}{column}", isMist ? mist : block, at);
            }
        }

        var corner = At(-FixtureMeshes.CrateSize, -FixtureMeshes.CrateSize);
        city.Interior.Place("C_Pad", city.AddModeledStatic("C_PadBase", FixtureMeshes.PadModel), corner - new Vector3(PadDistance, 0, 0));
        var crate = baseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel);
        target.Interior.PlaceCopyOf(baseGame.Interior.Place("B_Hidden", crate, HiddenAt)).MajorRecordFlagsRaw |= InitiallyDisabled;
        MoveCopy(target.Interior.PlaceCopyOf(baseGame.Interior.Place("B_Moved", crate, MovedFrom)), MovedTo);
        return [baseGame.Mod, target.Mod, city.Mod];
    }

    /// <summary>How many of the cleaned mod's crates stand in a row in <see cref="CreateRowWithALoneCrate"/>, each with a city pad beside it.</summary>
    public const int RowCrates = 20;

    /// <summary>Where the cleaned mod's crate without a pad stands, far from the row, and its marker beside it, east of the crate.</summary>
    public static readonly Vector3 LoneCrateAt = At(0, 5000);
    public static readonly Vector3 MarkerAt = At(300, 5000);

    /// <summary>Where the base-game rock the cleaned mod disabled stands, far from every crate.</summary>
    public static readonly Vector3 FarRockAt = At(10000, 8000);

    private const float RowSpacing = 1000f;

    /// <summary>
    /// The interior, where the cleaned mod placed <see cref="RowCrates"/> crates in a row, each with a city pad beside it (so they are
    /// removed as too close), one more crate far from the row with nothing beside it, and an XMarker beside that crate; it also
    /// disabled a rock of the base game far from all of them. The interior is one area: one of the cleaned mod's 21 crates stands.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateRowWithALoneCrate(string dataFolder) => BuildRowWithALoneCrate(dataFolder).LoadOrder;

    /// <summary>
    /// <see cref="CreateRowWithALoneCrate"/> with a base-game floor under the lone crate and a base-game pad resting on top of it,
    /// which the area method's removal of the crate leaves without support.
    /// </summary>
    public static (IReadOnlyList<ISkyrimMod> LoadOrder, PlacedObject PadOnLoneCrate) CreateRowWithALoneCrateCarryingAPad(string dataFolder)
    {
        var (loadOrder, baseGame) = BuildRowWithALoneCrate(dataFolder);
        var floor = baseGame.AddModeledStatic("B_FloorBase", FixtureMeshes.BuildingModel);
        baseGame.Interior.Place("B_Floor", floor, LoneCrateAt - new Vector3(0, 0, FixtureMeshes.BuildingHeight));
        var pad = baseGame.Interior.Place("B_PadOnLoneCrate", baseGame.AddModeledStatic("B_PadOnLoneCrateBase", FixtureMeshes.PadModel), LoneCrateAt + new Vector3(0, 0, FixtureMeshes.CrateSize));
        return (loadOrder, pad);
    }

    private static (IReadOnlyList<ISkyrimMod> LoadOrder, FixtureMod BaseGame) BuildRowWithALoneCrate(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var (baseGame, target, city, _) = CreateMods();
        var own = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        var pad = city.AddModeledStatic("C_PadBase", FixtureMeshes.PadModel);
        for (var index = 0; index < RowCrates; index++)
        {
            var at = At(index * RowSpacing, 0);
            target.Interior.Place($"T_Crate{index:00}", own, at);
            city.Interior.Place($"C_Pad{index:00}", pad, at + new Vector3(PadDistance, 0, 0));
        }

        target.Interior.Place("T_LoneCrate", own, LoneCrateAt);
        target.Interior.Place("T_Marker", target.AddMarkerStatic("T_XMarkerBase"), MarkerAt);
        var rock = baseGame.AddModeledStatic("B_RockBase", FixtureMeshes.CrateModel);
        target.Interior.PlaceCopyOf(baseGame.Interior.Place("B_Rock", rock, FarRockAt)).MajorRecordFlagsRaw |= InitiallyDisabled;
        return ([baseGame.Mod, target.Mod, city.Mod], baseGame);
    }

    /// <summary>Another mod whose camp is cleaned in a later step of the same group, in <see cref="CreateCampfireRingWithAnotherCamp"/>.</summary>
    public const string OtherCampPlugin = "OtherCamp.esp";

    /// <summary>In the exterior cell (0, 0): the cleaned mod's crate the city pad stands beside, its campfire, and the rock.</summary>
    public static readonly Vector3 CampCrateAt = At(800, 800);
    public static readonly Vector3 CampfireAt = At(1900, 800);
    public static readonly Vector3 RingRockFrom = At(500, 2000);
    public static readonly Vector3 RingRockTo = At(2050, 800);

    /// <summary>The Has Distant LOD flag of a static, as a large landmark such as a bridge carries it.</summary>
    private const int HasDistantLod = 0x8000;

    /// <summary>
    /// The exterior cell (0, 0), where the cleaned mod has a crate with a city pad beside it, which is removed, and a campfire 1100 units
    /// east of it, which stays. It moved a base game rock from 1200 units north of the crate into a ring 150 units east of the campfire.
    /// With a search radius of 1500 units the rock's old place sees only the removed crate, and its new place sees the campfire: the rock
    /// goes back and the patch adds a copy where the campfire's ring holds it.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateCampfireRing(string dataFolder) => BuildCampfireRing(dataFolder, rockHasDistantLod: false).LoadOrder;

    /// <summary>
    /// <see cref="CreateCampfireRing"/> with a rock whose base has distant LOD, as a large landmark such as a bridge has: it may not be
    /// copied, so it stays at the campfire.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateCampfireRingWithALandmark(string dataFolder) => BuildCampfireRing(dataFolder, rockHasDistantLod: true).LoadOrder;

    /// <summary>
    /// <see cref="CreateCampfireRing"/> with <see cref="OtherCampPlugin"/>, which loads after the city mod and places a tent 60 units
    /// north of where the cleaned mod put the rock: too close to the rock's copy by the collision settings of any cleaned mod's objects.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateCampfireRingWithAnotherCamp(string dataFolder)
    {
        var (loadOrder, baseGame, spaces) = BuildCampfireRing(dataFolder, rockHasDistantLod: false);
        var otherCamp = new FixtureMod(OtherCampPlugin, baseGame);
        spaces.AddOverridesTo(otherCamp);
        otherCamp.Exterior.Place("O_Tent", otherCamp.AddModeledStatic("O_TentBase", FixtureMeshes.CrateModel), RingRockTo + new Vector3(0, 60, 0));
        return [.. loadOrder, otherCamp.Mod];
    }

    /// <summary>A plugin that needs the cleaned mod and lists its campfire in a form list, so no step removes the campfire.</summary>
    public const string CampKeeperPlugin = "CampKeeper.esp";

    /// <summary>In the exterior cell (0, 0): the kept campfire of the cleared camp, and where the rock stood and stands.</summary>
    public static readonly Vector3 KeptCampfireAt = At(2000, 2000);
    public static readonly Vector3 CampRockFrom = At(500, 300);
    public static readonly Vector3 CampRockTo = At(2150, 2000);

    /// <summary>
    /// The exterior cell (0, 0), where the cleaned mod built a camp of three buildings around a campfire, each building with a city pad
    /// inside it, and a fourth building far south-west, with a pad too: all buildings are removed. A plugin that needs the cleaned mod
    /// lists the campfire in a form list, so it stays. The cleaned mod moved a base game rock from beside the fourth building to 150
    /// units east of the campfire. Both the rock's old place and its new one count as cleared (the campfire is small beside the
    /// buildings), and the left-behind step measures the rock around the campfire, which stands: the rock goes back with a copy left at
    /// the campfire, unless <paramref name="persistentRock"/>, which may not be copied and is held there.
    /// </summary>
    public static IReadOnlyList<ISkyrimMod> CreateClearedCampWithAKeptCampfire(string dataFolder, bool persistentRock)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var (baseGame, target, city, spaces) = CreateMods();
        var keeper = new FixtureMod(CampKeeperPlugin, baseGame, target);
        spaces.AddOverridesTo(keeper);
        var building = target.AddModeledStatic("T_BuildingBase", FixtureMeshes.BuildingModel);
        var pad = city.AddModeledStatic("C_PadBase", FixtureMeshes.PadModel);
        Vector3[] buildings = [At(1400, 2000), At(2600, 2000), At(2000, 1400), At(700, 700)];
        foreach (var (at, index) in buildings.Select((at, index) => (at, index)))
        {
            target.Exterior.Place($"T_Building{index}", building, at);
            city.Exterior.Place($"C_Pad{index}", pad, at + new Vector3(PadDistance, 0, 0));
        }
        var campfire = target.Exterior.Place("T_Campfire", target.AddModeledStatic("T_CampfireBase", FixtureMeshes.CrateModel), KeptCampfireAt);
        keeper.AddFormList("K_KeepList").Items.Add(new FormLink<ISkyrimMajorRecordGetter>(campfire.FormKey));
        var rock = baseGame.Exterior.Place("B_CampRock", baseGame.AddModeledStatic("B_CampRockBase", FixtureMeshes.CrateModel), CampRockFrom, persistent: persistentRock);
        if (persistentRock) rock.MajorRecordFlagsRaw |= (int)PlacedObject.DefaultMajorFlag.Persistent;
        MoveCopy(target.Exterior.PlaceCopyOf(rock, persistent: persistentRock), CampRockTo);
        return [baseGame.Mod, target.Mod, city.Mod, keeper.Mod];
    }

    private static (IReadOnlyList<ISkyrimMod> LoadOrder, FixtureMod BaseGame, FixtureSpaces Spaces) BuildCampfireRing(string dataFolder, bool rockHasDistantLod)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var (baseGame, target, city, spaces) = CreateMods();
        var own = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        target.Exterior.Place("T_CampCrate", own, CampCrateAt);
        city.Exterior.Place("C_Pad", city.AddModeledStatic("C_PadBase", FixtureMeshes.PadModel), CampCrateAt + new Vector3(PadDistance, 0, 0));
        target.Exterior.Place("T_Campfire", own, CampfireAt);
        var rockBase = baseGame.AddModeledStatic("B_RingRockBase", FixtureMeshes.CrateModel);
        if (rockHasDistantLod) baseGame.Mod.Statics.First(record => record.FormKey == rockBase).MajorRecordFlagsRaw |= HasDistantLod;
        MoveCopy(target.Exterior.PlaceCopyOf(baseGame.Exterior.Place("B_RingRock", rockBase, RingRockFrom)), RingRockTo);
        return ([baseGame.Mod, target.Mod, city.Mod], baseGame, spaces);
    }

    private static (FixtureMod BaseGame, FixtureMod Target, FixtureMod City, FixtureSpaces Spaces) CreateMods()
    {
        var baseGame = new FixtureMod(BaseGamePlugin);
        var target = new FixtureMod(FixtureWorld.TargetName, baseGame);
        var city = new FixtureMod(CityPlugin, baseGame);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        spaces.AddOverridesTo(target);
        spaces.AddOverridesTo(city);
        return (baseGame, target, city, spaces);
    }

    /// <summary>The cleaned mod changes the music, the location and the lighting; the city mod carries them and changes the music again.</summary>
    private static void ChangeTheCellSettings(FixtureMod baseGame, FixtureMod target, FixtureMod city)
    {
        var (calm, tense, epic) = (AddMusic(baseGame, "B_Calm"), AddMusic(baseGame, "B_Tense"), AddMusic(baseGame, "B_Epic"));
        var (town, village) = (AddLocation(baseGame, "B_Town"), AddLocation(baseGame, "B_Village"));
        Settle(baseGame.Interior.Record, calm, town, BaseFogNear);
        Settle(target.Interior.Record, tense, village, CleanedModFogNear);
        Settle(city.Interior.Record, epic, village, CleanedModFogNear);
    }

    private static void Settle(Cell cell, FormKey music, FormKey location, float fogNear)
    {
        cell.Music.SetTo(music);
        cell.Location.SetTo(location);
        cell.Lighting = new CellLighting { FogNear = fogNear };
    }

    private static FormKey AddMusic(FixtureMod mod, string editorId)
    {
        var record = new MusicType(mod.NextKey(), FixtureMod.Release) { EditorID = editorId };
        mod.Mod.MusicTypes.Add(record);
        return record.FormKey;
    }

    private static FormKey AddLocation(FixtureMod mod, string editorId)
    {
        var record = new Location(mod.NextKey(), FixtureMod.Release) { EditorID = editorId };
        mod.Mod.Locations.Add(record);
        return record.FormKey;
    }

    private static void DeleteInTarget(FixtureMod target, PlacedObject original)
    {
        var deleted = target.Interior.PlaceCopyOf(original);
        deleted.IsDeleted = true;
        deleted.Placement = null;
    }

    /// <summary>The cleaned mod turns and resizes the object; the city mod carries the turn and resizes it again.</summary>
    private static void TurnAndResizeInBothMods(PlacedObject original, FixtureMod target, FixtureMod city)
    {
        var cleaned = target.Interior.PlaceCopyOf(original);
        cleaned.Placement!.Rotation = new P3Float(0, 0, CleanedModTurn);
        cleaned.Scale = CleanedModScale;
        city.Interior.PlaceCopyOf(cleaned).Scale = CityScale;
    }

    private static void MoveCopy(PlacedObject copy, Vector3 to) => copy.Placement!.Position = new P3Float(to.X, to.Y, to.Z);

    private static Vector3 At(float x, float y) => new(x, y, 0);

    public static Landscape FlatLand(FormKey key, float steps) => new(key, FixtureMod.Release)
    {
        VertexHeightMap = new LandscapeVertexHeightMap { Offset = steps, HeightMap = new Array2d<sbyte>(new P2Int(Side, Side), default) },
        VertexNormals = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
        VertexColors = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
    };
}
