using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Another mod's object judged by the accepted-value rule through a whole run, with the trust settings' precedence, on a small world
/// of its own: the cleaned mod's crate in the interior and a base game wall far from it. The first plugin that overrides the wall moves
/// it 5 units from the crate; every later one carries the move.
/// </summary>
public class OtherObjectTrustRunTests
{
    private const int Workers = 4;
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string MoverPlugin = "Mover.esp";
    private const string CarrierPlugin = "Carrier.esp";
    private const string EarlyMoverPlugin = "EarlyMover.esp";
    private const string EarlyCarrierPlugin = "EarlyCarrier.esp";
    private const float FarCentre = 2000f;

    /// <summary>The wall's near side 5 units from the 100-unit crate: inside the size margin of 10 units.</summary>
    private const float NearCentre = 75f;

    [Fact]
    public void AnUntrustedModMovingABaseGameWallNextToTheCrateRemovesItAndIsNamed()
    {
        var outcome = Run(new TrustSettings(), beforeCrate: [], afterCrate: [MoverPlugin]);

        Assert.Single(outcome.Final.RemovedTargets());
        Assert.Equal(new ObjectChange(Plugin(MoverPlugin), Plugin(BaseGamePlugin)), Assert.Single(outcome.Collected.OtherModObjects).Change);
    }

    [Fact]
    public void ATrustedModCarryingTheMoveMakesItAcceptedSoTheCrateStays()
    {
        var outcome = Run(new TrustSettings { TrustedModsAndTheirMasters = [Key(CarrierPlugin)] }, beforeCrate: [], afterCrate: [MoverPlugin, CarrierPlugin]);

        Assert.Empty(outcome.Final.RemovedTargets());
        Assert.Empty(outcome.Collected.OtherModObjects);
    }

    /// <summary>The carrier is listed as trusted and as never trusted: never trusted wins, and the mover, not the carrier, is named.</summary>
    [Fact]
    public void NeverTrustedBeatsTheTrustedListForACarriedMove()
    {
        var trust = new TrustSettings { TrustedModsAndTheirMasters = [Key(CarrierPlugin)], NeverTrustedMods = [Key(CarrierPlugin)] };

        var outcome = Run(trust, beforeCrate: [], afterCrate: [MoverPlugin, CarrierPlugin]);

        Assert.Single(outcome.Final.RemovedTargets());
        Assert.Equal(new ObjectChange(Plugin(MoverPlugin), Plugin(BaseGamePlugin)), Assert.Single(outcome.Collected.OtherModObjects).Change);
    }

    /// <summary>
    /// Both plugins load before the cleaned mod; the mover is never trusted. The carrier is trusted by the rule for mods loading
    /// before the cleaned mod, unless it is listed as trusted for its own changes only, which beats that rule: then it owns nothing
    /// it only carries, and the mover's move is a change.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrustedForOwnChangesOnlyBeatsTheRuleForModsLoadingBeforeTheCleanedMod(bool carrierListedForOwnChanges)
    {
        var trust = new TrustSettings
        {
            NeverTrustedMods = [Key(EarlyMoverPlugin)],
            ModsTrustedForOwnChangesOnly = carrierListedForOwnChanges ? [Key(EarlyCarrierPlugin)] : [],
        };

        var outcome = Run(trust, beforeCrate: [EarlyMoverPlugin, EarlyCarrierPlugin], afterCrate: []);

        Assert.Equal(carrierListedForOwnChanges ? 1 : 0, outcome.Final.RemovedTargets().Length);
        ObjectChange?[] expected = carrierListedForOwnChanges ? [new ObjectChange(Plugin(EarlyMoverPlugin), Plugin(BaseGamePlugin))] : [];
        Assert.Equal(expected, outcome.Collected.OtherModObjects.Select(other => other.Change));
    }

    private static ModKey Key(string plugin) => ModKey.FromNameAndExtension(plugin);

    private static PluginName Plugin(string plugin) => new(plugin);

    private static RunOutcome Run(TrustSettings trust, IReadOnlyList<string> beforeCrate, IReadOnlyList<string> afterCrate) =>
        FixtureRun.Execute(TooCloseWith(trust), Workers, dataFolder => CreateWorld(dataFolder, beforeCrate, afterCrate));

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

    /// <param name="beforeCrate">Plugins loading before the cleaned mod that override the wall, in load order.</param>
    /// <param name="afterCrate">Plugins loading after the cleaned mod that override the wall, in load order.</param>
    private static IReadOnlyList<ISkyrimMod> CreateWorld(string dataFolder, IReadOnlyList<string> beforeCrate, IReadOnlyList<string> afterCrate)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod(BaseGamePlugin);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        var wall = baseGame.Interior.Place("Sky_Wall", baseGame.AddModeledStatic("Sky_WallBase", FixtureMeshes.PadModel), new Vector3(FarCentre, 0, 0));
        var target = new FixtureMod(FixtureWorld.TargetName, baseGame);
        spaces.AddOverridesTo(target);
        target.Interior.Place("T_Crate", target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel), Vector3.Zero);

        var before = beforeCrate.Select(name => MoveWall(name, baseGame, spaces, wall)).ToList();
        var after = afterCrate.Select(name => MoveWall(name, baseGame, spaces, wall)).ToList();
        return [baseGame.Mod, .. before, target.Mod, .. after];
    }

    private static ISkyrimMod MoveWall(string plugin, FixtureMod baseGame, FixtureSpaces spaces, PlacedObject wall)
    {
        var mover = new FixtureMod(plugin, baseGame);
        spaces.AddOverridesTo(mover);
        mover.Interior.PlaceCopyOf(wall).Placement!.Position = new P3Float(NearCentre, 0, 0);
        return mover.Mod;
    }
}
