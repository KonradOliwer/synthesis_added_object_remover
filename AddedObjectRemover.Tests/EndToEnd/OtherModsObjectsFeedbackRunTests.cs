using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// A stack on a crate of the cleaned mod that is too close to an untrusted pad, through a whole run with PullObjectToTheGround
/// and OnlyWhatLosesItsSupport: on a base game floor the crate, on it a trusted pad, on that a pad of the cleaned mod, and on that
/// another trusted pad. The crate is removed, the lower trusted pad is pulled down to the floor, the cleaned mod's pad goes with
/// the support it lost, and the upper trusted pad is pulled down onto the lower one where it lands.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public class OtherModsObjectsFeedbackRunTests
{
    private const int ManyWorkers = 4;
    private const int OneWorker = 1;
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string TrustedPlugin = "Trusted.esp";
    private const string UntrustedPlugin = "Untrusted.esp";
    private const float CrateTop = FixtureMeshes.CrateSize;
    private const float PadHeight = FixtureMeshes.PadHeight;

    /// <summary>Beside the crate, still 5 units into its footprint after shrinking to 80%: no size from 80% to 100% fits.</summary>
    private const float OverlappingCauseX = 45f;

    [Fact]
    public void OutputMatchesExpectedOutput()
    {
        var (run, _) = Execute(ManyWorkers);

        ExpectedOutputFiles.AssertMatches("other-mods-objects/Feedback", run.ToExpectedOutputLines());
    }

    [Fact]
    public void OneAndManyWorkersGiveTheSameOutput()
    {
        Assert.Equal(Execute(OneWorker).Run.ToExpectedOutputLines(), Execute(ManyWorkers).Run.ToExpectedOutputLines());
    }

    [Fact]
    public void TheCleanedModsPadOnAPulledPadIsRemovedAndThePadOnItLandsOnThePulledOne()
    {
        var (run, scene) = Execute(ManyWorkers);

        Assert.Contains("disabled True", Line(run, scene.Crate));
        Assert.Contains("disabled True", Line(run, scene.CleanedModPad));
        Assert.Contains("disabled False, position (0.00, 0.00, 0.00)", Line(run, scene.LowerPad));
        Assert.Contains($"disabled False, position (0.00, 0.00, {PadHeight:F2})", Line(run, scene.UpperPad));
    }

    private static string Line(RunOutput run, PlacedObject record) => Assert.Single(run.Patch, line => line.StartsWith(record.FormKey + " in ", StringComparison.Ordinal));

    private static (RunOutput Run, Scene Scene) Execute(int workers)
    {
        Scene? scene = null;
        var run = PipelineRun.Execute(CreateSettings(), workers, dataFolder => (scene = new Scene(dataFolder)).LoadOrder);
        return (run, scene!);
    }

    private static Settings CreateSettings() => new()
    {
        DetailedLogAndReports = true,
        Standard = new StandardSettings
        {
            CleanedMod = ModKey.FromNameAndExtension(FixtureWorld.TargetName),
            TooClose = new TooCloseRemovalSettings
            {
                BySize = new TooCloseBySizeSettings { Use = true, SizeMultiplier = 0.1f },
                ByDistance = new TooCloseByDistanceSettings { Use = false },
            },
            TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = false },
            Trust = new TrustSettings
            {
                TrustedModsAndTheirMasters = [ModKey.FromNameAndExtension(TrustedPlugin)],
                NeverTrustedMods = [ModKey.FromNameAndExtension(UntrustedPlugin)],
                TrustModsRequiringCleanedModAndTheirMasters = false,
                TrustModsLoadingBeforeCleanedMod = false,
            },
            ShrinkOrTurn = new ShrinkOrTurnSettings { ShrinkLimitPercent = 80, TurnLimitDegrees = 0 },
            FollowUpRemoval = new FollowUpRemovalSettings
            {
                Mode = FollowUpRemovalMode.OnlyWhatLosesItsSupport,
                OtherModsObjectsLeftWithoutSupport = OtherModsObjectsHandling.PullObjectToTheGround,
            },
        },
    };

    /// <summary>The world, in load order: Skyrim.esm, Target.esp, Trusted.esp, Untrusted.esp.</summary>
    private sealed class Scene
    {
        public Scene(string dataFolder)
        {
            FixtureMeshes.WriteAll(dataFolder);
            var baseGame = new FixtureMod(BaseGamePlugin);
            var spaces = FixtureSpaces.CreateIn(baseGame);
            var target = Create(spaces, FixtureWorld.TargetName, baseGame);
            var trusted = Create(spaces, TrustedPlugin, baseGame);
            var untrusted = Create(spaces, UntrustedPlugin, baseGame);
            LoadOrder = [baseGame.Mod, target.Mod, trusted.Mod, untrusted.Mod];

            baseGame.Interior.Place("Sky_Floor", baseGame.AddModeledStatic("Sky_FloorBase", FixtureMeshes.BuildingModel), new Vector3(0f, 0f, -FixtureMeshes.BuildingHeight));
            Crate = target.Interior.Place("T_Crate", target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel), Vector3.Zero);
            var trustedPad = trusted.AddModeledStatic("Trusted_PadBase", FixtureMeshes.PadModel);
            LowerPad = trusted.Interior.Place("Trusted_LowerPad", trustedPad, new Vector3(0f, 0f, CrateTop));
            CleanedModPad = target.Interior.Place("T_Pad", target.AddModeledStatic("T_PadBase", FixtureMeshes.PadModel), new Vector3(0f, 0f, CrateTop + PadHeight));
            UpperPad = trusted.Interior.Place("Trusted_UpperPad", trustedPad, new Vector3(0f, 0f, CrateTop + 2 * PadHeight));
            untrusted.Interior.Place("U_Cause", untrusted.AddModeledStatic("U_CauseBase", FixtureMeshes.PadModel), new Vector3(OverlappingCauseX, 0f, 0f));
        }

        public IReadOnlyList<ISkyrimMod> LoadOrder { get; }

        public PlacedObject Crate { get; }

        public PlacedObject LowerPad { get; }

        public PlacedObject CleanedModPad { get; }

        public PlacedObject UpperPad { get; }

        private static FixtureMod Create(FixtureSpaces spaces, string plugin, params FixtureMod[] masters)
        {
            var mod = new FixtureMod(plugin, masters);
            spaces.AddOverridesTo(mod);
            return mod;
        }
    }
}
