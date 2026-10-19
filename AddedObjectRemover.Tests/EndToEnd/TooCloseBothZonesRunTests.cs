using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The too-close step with both removal zones on, through a whole run, on a small world of its own: three target crates in one
/// interior, each with another mod's pad to its east. The size zone (0.1 × the crate's size, 10 units) finds the nearest pad,
/// the distance zone (32 units) the next, and the third pad is out of reach of both.
/// </summary>
public class TooCloseBothZonesRunTests
{
    private const int Workers = 4;
    private const float SizeMultiplier = 0.1f;

    private const float DistanceMargin = 32f;

    [Fact]
    public void EachZoneRemovesTheCrateItFindsWithItsOwnCause()
    {
        var outcome = ((RunResult.Done)FixtureRun.Run(BothZones(), Workers, new RecordingLogSink(), DefaultMeshFiles, createLoadOrder: CrateWorld.Create)).Outcome;

        Assert.Equal(
            new Dictionary<string, TooCloseCause?>
            {
                [CrateWorld.CrateFoundBySize] = new TooCloseCause.ObjectSize(),
                [CrateWorld.CrateFoundByDistance] = new TooCloseCause.Distance(DistanceMargin),
                [CrateWorld.CrateOutOfReach] = null,
            },
            outcome.Collected.Targets.ToDictionary(
                target => target.EditorId!,
                target => outcome.Final.Of(target.Id) is Decision.Removed { Reason: RemovalReason.TooClose tooClose } ? tooClose.Cause : null));
    }

    [Fact]
    public void TheLogCountsEachZoneAndWordsEachRemovalByItsZone()
    {
        var settings = BothZones();
        settings.DetailedLogAndReports = true;
        var log = new RecordingLogSink();

        FixtureRun.Run(settings, Workers, log, DefaultMeshFiles, createLoadOrder: CrateWorld.Create);

        Assert.Contains("Margin by fixed distance: on, 32 units;", Assert.Single(SectionLines(log, "config"), line => line.StartsWith("Remove objects too close")));
        var tooClose = SectionLines(log, "tooClose");
        Assert.StartsWith(
            "Object-shape zones: 2 candidate pairs, 2 passed the box filter, 2 mesh tests, 2 hits, 0 centre-point fallbacks (other object without mesh triangles), "
            + "0 targets without mesh triangles used their box;",
            tooClose[0]);
        Assert.Contains(
            $"Found 2 of 3 {FixtureWorld.TargetPlugin} objects too close to other mods' objects (1 by object size, 1 by distance) in 0.0s.", tooClose);
        Assert.Equal(
            ["Removed 2 objects (2 too close, 0 follow-up removals, 0 leftover invisible objects, 0 linked to removed objects)."],
            SectionLines(log, "removal-summary"));
        var removals = SectionLines(log, "removals");
        Assert.Single(removals, line => line.Contains(CrateWorld.CrateFoundBySize) && line.Contains($"; too close to {CrateWorld.PadNearBySize} "));
        Assert.Single(removals, line => line.Contains(CrateWorld.CrateFoundByDistance) && line.Contains($"; within 32 units of {CrateWorld.PadWithinDistance} "));
    }

    private static Settings BothZones() => new()
    {
        Standard = new StandardSettings
        {
            TargetPlugin = ModKey.FromNameAndExtension(FixtureWorld.TargetPlugin),
            TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = false },
            TooClose = new TooCloseRemovalSettings
            {
                BySize = new TooCloseBySizeSettings { Use = true, SizeMultiplier = SizeMultiplier },
                ByDistance = new TooCloseByDistanceSettings { Use = true, DistanceInUnits = DistanceMargin },
            },
        },
    };

    private static IMeshFilesFactory DefaultMeshFiles(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    private static string[] SectionLines(RecordingLogSink log, string id) =>
        [.. log.Sections.Where(section => section.Id == id).SelectMany(section => section.Lines)];

    /// <summary>The crates are 100 units wide and the pads 40, so a pad's near side is 20 units closer than its centre.</summary>
    private static class CrateWorld
    {
        public const string CrateFoundBySize = "T_CrateFoundBySize";
        public const string CrateFoundByDistance = "T_CrateFoundByDistance";
        public const string CrateOutOfReach = "T_CrateOutOfReach";
        public const string PadNearBySize = "P_PadNearBySize";
        public const string PadWithinDistance = "P_PadWithinDistance";

        private const string BaseGamePlugin = "Skyrim.esm";
        private const string OtherPlugin = "Other.esp";
        private const float RowSpacing = 1000f;

        /// <summary>The pad's near side 5 units from the crate: inside the size zone's 10 units.</summary>
        private const float SizeZonePadCentre = 75f;

        /// <summary>The pad's near side 25 units from the crate: beyond the size zone, within the distance zone's 32 units.</summary>
        private const float DistanceZonePadCentre = 95f;

        /// <summary>The pad's near side 130 units from the crate: beyond both zones.</summary>
        private const float FarPadCentre = 200f;

        public static IReadOnlyList<ISkyrimMod> Create(string dataFolder)
        {
            FixtureMeshes.WriteAll(dataFolder);
            var baseGame = new FixtureMod(BaseGamePlugin);
            var target = new FixtureMod(FixtureWorld.TargetPlugin, baseGame);
            var other = new FixtureMod(OtherPlugin, baseGame);
            var spaces = FixtureSpaces.CreateIn(baseGame);
            spaces.AddOverridesTo(target);
            spaces.AddOverridesTo(other);

            var crate = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
            var pad = other.AddModeledStatic("P_PadBase", FixtureMeshes.PadModel);
            (string Crate, string Pad, float PadCentre)[] rows =
            [
                (CrateFoundBySize, PadNearBySize, SizeZonePadCentre),
                (CrateFoundByDistance, PadWithinDistance, DistanceZonePadCentre),
                (CrateOutOfReach, "P_PadOutOfReach", FarPadCentre),
            ];
            for (var row = 0; row < rows.Length; row++)
            {
                var y = row * RowSpacing;
                target.Interior.Place(rows[row].Crate, crate, new Vector3(0, y, 0));
                other.Interior.Place(rows[row].Pad, pad, new Vector3(rows[row].PadCentre, y, 0));
            }
            return [baseGame.Mod, target.Mod, other.Mod];
        }
    }
}
