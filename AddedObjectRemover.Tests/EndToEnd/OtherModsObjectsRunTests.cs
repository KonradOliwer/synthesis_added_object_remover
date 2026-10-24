using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Other mods' objects resting on the cleaned mod's crates, through a whole run on a small world of its own, for each value of the
/// setting. Seven columns in the interior, each with a crate of the cleaned mod on a base game floor (none in one column) and, in
/// all but the last, a pad on the crate: of a trusted mod (crate removed), of a trusted mod (shrinking would make the crate fit, but
/// the pad rests on it, so it is removed), of a trusted mod with no floor below (crate removed), of an untrusted mod, of a plugin
/// that is only a master of the trusted mod, and of a master of the cleaned mod (crate removed); the last crate has nothing on it
/// and is shrunk. An untrusted pad next to each crate makes it too close; in the untrusted column the resting pad itself does.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public class OtherModsObjectsRunTests
{
    private const int ManyWorkers = 4;
    private const int OneWorker = 1;
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string CleanedModMasterPlugin = "TargetMaster.esm";
    private const string TrustedMasterPlugin = "TrustedBase.esm";
    private const string TrustedPlugin = "Trusted.esp";
    private const string UntrustedPlugin = "Untrusted.esp";
    private const float ColumnSpacing = 1000f;
    private const float FloorTop = 0f;
    private const float CrateTop = FixtureMeshes.CrateSize;

    /// <summary>Beside the crate, still 5 units into its footprint after shrinking to 80%: no size from 80% to 100% fits.</summary>
    private const float OverlappingCauseX = 45f;

    /// <summary>The pad's near side 5 units from the crate: shrinking the crate to 90% makes it fit.</summary>
    private const float NearCauseX = 75f;

    private const string DisabledMark = "disabled True";
    private const string EnabledMark = "disabled False";

    private static readonly Column[] Judged = [Column.Removed, Column.NotShrunkPadOnIt, Column.NothingBelow, Column.CleanedModMaster];
    private static readonly Column[] Ignored = [Column.UntrustedPad, Column.MasterOfTrustedMod];

    public static TheoryData<OtherModsObjectsHandling> Handlings => new(Enum.GetValues<OtherModsObjectsHandling>());

    [Theory]
    [MemberData(nameof(Handlings))]
    public void OutputMatchesExpectedOutput(OtherModsObjectsHandling handling)
    {
        var (run, _) = Execute(handling, ManyWorkers);

        ExpectedOutputFiles.AssertMatches($"other-mods-objects/{handling}", run.ToExpectedOutputLines());
    }

    [Theory]
    [MemberData(nameof(Handlings))]
    public void OneAndManyWorkersGiveTheSameOutput(OtherModsObjectsHandling handling)
    {
        Assert.Equal(Execute(handling, OneWorker).Run.ToExpectedOutputLines(), Execute(handling, ManyWorkers).Run.ToExpectedOutputLines());
    }

    [Fact]
    public void PullObjectToTheGroundPullsTheJudgedObjectsDownKeepingTheirRotationAndRemovesTheOneWithNothingBelow()
    {
        var (run, scene) = Execute(OtherModsObjectsHandling.PullObjectToTheGround, ManyWorkers);

        Assert.Contains(DisabledMark, Line(run, scene.Crates[Column.Removed]));
        Assert.Contains(DisabledMark, Line(run, scene.Crates[Column.NotShrunkPadOnIt]));
        AssertPulledTo(run, scene, Column.Removed, FloorTop);
        AssertPulledTo(run, scene, Column.NotShrunkPadOnIt, FloorTop);
        AssertPulledTo(run, scene, Column.CleanedModMaster, FloorTop);
        Assert.Contains(DisabledMark, Line(run, scene.Pads[Column.NothingBelow]));
        AssertIgnoredObjectsAreUntouched(run, scene);
    }

    [Theory]
    [MemberData(nameof(Handlings))]
    public void OnlyACrateWithNothingOnItIsShrunkWithEveryChoice(OtherModsObjectsHandling handling)
    {
        var (run, scene) = Execute(handling, ManyWorkers);

        Assert.Contains($"{EnabledMark}, position ({ColumnX(Column.ShrunkNothingOnIt):F2}, 0.00, 0.00)", Line(run, scene.Crates[Column.ShrunkNothingOnIt]));
        Assert.Contains($"Kept shrunk or turned: T_Crate_{Column.ShrunkNothingOnIt} ", string.Join('\n', run.Log));
        Assert.DoesNotContain($"Kept shrunk or turned: T_Crate_{Column.NotShrunkPadOnIt} ", string.Join('\n', run.Log));
    }

    [Fact]
    public void RemoveFloatingObjectRemovesTheJudgedObjects()
    {
        var (run, scene) = Execute(OtherModsObjectsHandling.RemoveFloatingObject, ManyWorkers);

        Assert.All(Judged, column => Assert.Contains(DisabledMark, Line(run, scene.Pads[column])));
        Assert.Contains(DisabledMark, Line(run, scene.Crates[Column.Removed]));
        Assert.Contains(DisabledMark, Line(run, scene.Crates[Column.NotShrunkPadOnIt]));
        AssertIgnoredObjectsAreUntouched(run, scene);
    }

    [Fact]
    public void KeepSupportingObjectLeavesTheCratesJudgedObjectsRestOnAsTheyAre()
    {
        var (run, scene) = Execute(OtherModsObjectsHandling.KeepSupportingObject, ManyWorkers);

        Assert.All(Judged, column => Assert.False(HasLine(run, scene.Crates[column])));
        Assert.All(Judged, column => Assert.False(HasLine(run, scene.Pads[column])));
        AssertIgnoredObjectsAreUntouched(run, scene);
    }

    private static void AssertIgnoredObjectsAreUntouched(RunOutput run, Scene scene)
    {
        Assert.All(Ignored, column => Assert.Contains(DisabledMark, Line(run, scene.Crates[column])));
        Assert.All(Ignored, column => Assert.False(HasLine(run, scene.Pads[column])));
    }

    /// <summary>The pad is written at its column and the new height, with the rotation it had (none).</summary>
    private static void AssertPulledTo(RunOutput run, Scene scene, Column column, float height) =>
        Assert.Contains($"{EnabledMark}, position ({ColumnX(column):F2}, 0.00, {height:F2}), rotation (0.00, 0.00, 0.00)", Line(run, scene.Pads[column]));

    private static bool HasLine(RunOutput run, PlacedObject record) => run.Patch.Any(line => IsLineOf(line, record));

    private static string Line(RunOutput run, PlacedObject record) => Assert.Single(run.Patch, line => IsLineOf(line, record));

    private static bool IsLineOf(string line, PlacedObject record) => line.StartsWith(record.FormKey + " in ", StringComparison.Ordinal);

    private static float ColumnX(Column column) => (int)column * ColumnSpacing;

    private static (RunOutput Run, Scene Scene) Execute(OtherModsObjectsHandling handling, int workers)
    {
        Scene? scene = null;
        var run = PipelineRun.Execute(CreateSettings(handling), workers, dataFolder => (scene = new Scene(dataFolder)).LoadOrder);
        return (run, scene!);
    }

    private static Settings CreateSettings(OtherModsObjectsHandling handling) => new()
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
            FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.Nothing, OtherModsObjectsLeftWithoutSupport = handling },
        },
    };

    private enum Column
    {
        Removed,
        NotShrunkPadOnIt,
        NothingBelow,
        UntrustedPad,
        MasterOfTrustedMod,
        CleanedModMaster,
        ShrunkNothingOnIt,
    }

    /// <summary>The world, in load order: Skyrim.esm, TargetMaster.esm, TrustedBase.esm, Target.esp, Trusted.esp, Untrusted.esp.</summary>
    private sealed class Scene
    {
        public Scene(string dataFolder)
        {
            FixtureMeshes.WriteAll(dataFolder);
            var baseGame = new FixtureMod(BaseGamePlugin);
            var spaces = FixtureSpaces.CreateIn(baseGame);
            var cleanedModMaster = Create(spaces, CleanedModMasterPlugin, baseGame);
            var trustedMaster = Create(spaces, TrustedMasterPlugin, baseGame);
            var target = Create(spaces, FixtureWorld.TargetName, baseGame, cleanedModMaster);
            var trusted = Create(spaces, TrustedPlugin, baseGame, trustedMaster);
            var untrusted = Create(spaces, UntrustedPlugin, baseGame);
            LoadOrder = [baseGame.Mod, cleanedModMaster.Mod, trustedMaster.Mod, target.Mod, trusted.Mod, untrusted.Mod];

            var floor = baseGame.AddModeledStatic("Sky_FloorBase", FixtureMeshes.BuildingModel);
            var crate = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
            var padOwners = new Dictionary<Column, FixtureMod>
            {
                [Column.Removed] = trusted,
                [Column.NotShrunkPadOnIt] = trusted,
                [Column.NothingBelow] = trusted,
                [Column.UntrustedPad] = untrusted,
                [Column.MasterOfTrustedMod] = trustedMaster,
                [Column.CleanedModMaster] = cleanedModMaster,
            };
            var causeBase = untrusted.AddModeledStatic("U_CausePadBase", FixtureMeshes.PadModel);
            Crates = Enum.GetValues<Column>().ToDictionary(column => column, column => target.Interior.Place($"T_Crate_{column}", crate, At(column, 0f)));
            Pads = padOwners.ToDictionary(
                owner => owner.Key,
                owner => owner.Value.Interior.Place(
                    $"{owner.Key}_Pad", owner.Value.AddModeledStatic($"{owner.Key}_PadBase", FixtureMeshes.PadModel), At(owner.Key, CrateTop)));

            foreach (var column in Enum.GetValues<Column>().Where(column => column != Column.NothingBelow))
            {
                baseGame.Interior.Place($"Sky_Floor_{column}", floor, At(column, FloorTop - FixtureMeshes.BuildingHeight));
            }
            foreach (var column in Enum.GetValues<Column>())
            {
                var causeX = column is Column.NotShrunkPadOnIt or Column.ShrunkNothingOnIt ? NearCauseX : OverlappingCauseX;
                untrusted.Interior.Place($"U_Cause_{column}", causeBase, At(column, 0f) + new Vector3(causeX, 0f, 0f));
            }
        }

        public IReadOnlyList<ISkyrimMod> LoadOrder { get; }

        public IReadOnlyDictionary<Column, PlacedObject> Crates { get; }

        public IReadOnlyDictionary<Column, PlacedObject> Pads { get; }

        private static Vector3 At(Column column, float height) => new(ColumnX(column), 0f, height);

        private static FixtureMod Create(FixtureSpaces spaces, string plugin, params FixtureMod[] masters)
        {
            var mod = new FixtureMod(plugin, masters);
            spaces.AddOverridesTo(mod);
            return mod;
        }
    }
}
