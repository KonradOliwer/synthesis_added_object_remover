using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Which other mods can cause removals, through a whole run, on a small world of its own: the cleaned mod's crate in the interior with
/// a city mod's wall 5 units from it, and, in some runs, a compatibility patch that requires both.
/// </summary>
public class TrustRunTests
{
    private const int Workers = 4;
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string CityPlugin = "CityMod.esp";
    private const string CompatibilityPatchPlugin = "CityMod - Target Patch.esp";

    /// <summary>The wall's near side 5 units from the 100-unit crate: inside the size margin of 10 units.</summary>
    private const float WallCentre = 75f;

    [Fact]
    public void WithoutTrustTheCityModsWallRemovesTheCrate() =>
        Assert.Single(Run(new TrustSettings(), withCompatibilityPatch: false).Final.RemovedTargets());

    [Fact]
    public void ACompatibilityPatchThatRequiresTheCleanedModMakesTheCityModTrustedSoTheCrateStays() =>
        Assert.Empty(Run(new TrustSettings(), withCompatibilityPatch: true).Final.RemovedTargets());

    [Fact]
    public void ACityModListedAsTrustedLeavesTheCrate() =>
        Assert.Empty(Run(new TrustSettings { TrustedModsAndTheirMasters = [City] }, withCompatibilityPatch: false).Final.RemovedTargets());

    [Fact]
    public void NeverTrustedBeatsTheListOfTrustedMods() =>
        Assert.Single(Run(new TrustSettings { TrustedModsAndTheirMasters = [City], NeverTrustedMods = [City] }, withCompatibilityPatch: false).Final.RemovedTargets());

    [Fact]
    public void NeverTrustedBeatsTheRuleForModsThatRequireTheCleanedMod() =>
        Assert.Single(Run(new TrustSettings { NeverTrustedMods = [City] }, withCompatibilityPatch: true).Final.RemovedTargets());

    [Fact]
    public void ACompatibilityPatchCarryingThePatchMarkerDoesNotMakeTheCityModTrustedSoTheCrateIsRemoved() =>
        Assert.Single(Run(new TrustSettings(), withCompatibilityPatch: true, patchCarriesMarker: true).Final.RemovedTargets());

    [Fact]
    public void ACompatibilityPatchWithoutTheMarkerListedAsNeverTrustedDoesNotMakeTheCityModTrustedSoTheCrateIsRemoved() =>
        Assert.Single(Run(new TrustSettings { NeverTrustedMods = [CompatibilityPatch] }, withCompatibilityPatch: true).Final.RemovedTargets());

    private static ModKey City => ModKey.FromNameAndExtension(CityPlugin);

    private static ModKey CompatibilityPatch => ModKey.FromNameAndExtension(CompatibilityPatchPlugin);

    private static RunOutcome Run(TrustSettings trust, bool withCompatibilityPatch, bool patchCarriesMarker = false) =>
        FixtureRun.Execute(TooCloseWith(trust), Workers, dataFolder => CreateWorld(dataFolder, withCompatibilityPatch, patchCarriesMarker));

    private static Settings TooCloseWith(TrustSettings trust) => new()
    {
        Standard = new StandardSettings
        {
            CleanedMod = ModKey.FromNameAndExtension(FixtureWorld.TargetName),
            Trust = trust,
            TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = false },
            TooClose = new TooCloseRemovalSettings
            {
                BySize = new TooCloseBySizeSettings { Use = true, SizeMultiplier = 0.1f },
                ByDistance = new TooCloseByDistanceSettings { Use = true, DistanceInUnits = 32f },
            },
        },
    };

    /// <summary>Base game, cleaned mod and city mod, then the compatibility patch when asked for, with the patch marker in its description when asked for.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateWorld(string dataFolder, bool withCompatibilityPatch, bool patchCarriesMarker)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod(BaseGamePlugin);
        var target = new FixtureMod(FixtureWorld.TargetName, baseGame);
        var city = new FixtureMod(CityPlugin, baseGame);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        spaces.AddOverridesTo(target);
        spaces.AddOverridesTo(city);

        target.Interior.Place("T_Crate", target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel), Vector3.Zero);
        city.Interior.Place("C_Wall", city.AddModeledStatic("C_WallBase", FixtureMeshes.PadModel), new Vector3(WallCentre, 0, 0));
        IReadOnlyList<ISkyrimMod> plugins = [baseGame.Mod, target.Mod, city.Mod];
        return withCompatibilityPatch ? [.. plugins, CreateCompatibilityPatch(baseGame, target, city, patchCarriesMarker)] : plugins;
    }

    private static SkyrimMod CreateCompatibilityPatch(FixtureMod baseGame, FixtureMod target, FixtureMod city, bool carriesMarker)
    {
        var patch = new FixtureMod(CompatibilityPatchPlugin, baseGame, target, city).Mod;
        if (carriesMarker) patch.ModHeader.Description = PatchMarker.Text;
        return patch;
    }
}
