using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The mods of the ring world, in load order: Skyrim.esm, Target.esp, Trusted.esp, Untrusted.esp.</summary>
internal sealed record RingWorldMods(FixtureMod BaseGame, FixtureMod Target, FixtureMod Trusted, FixtureMod Untrusted);

/// <summary>
/// A small world of its own for the visible left-behind rounds. A crate of the cleaned mod stands in the middle; six more crates of the
/// cleaned mod stand around it in six directions, each too close to a pad of an untrusted mod. A run removes the six for being too close,
/// which leaves the middle crate with its surroundings removed.
/// </summary>
internal static class RingWorld
{
    public const string TrustedPlugin = "Trusted.esp";
    public const string MiddleCrate = "T_MiddleCrate";
    public const string DoorEditorId = "T_DoorOnMiddleCrate";
    private const string PadEditorId = "Trusted_PadOnDoor";
    public const float FloorTop = 0f;
    public const int RingCrates = 6;

    private const string BaseGamePlugin = "Skyrim.esm";
    private const string UntrustedPlugin = "Untrusted.esp";
    private const float RingDistance = 200f;
    private const float CauseShift = 45f;
    private const int FirstRingAngle = 45;
    private const int AngleStep = 45;

    /// <param name="surroundings">The visible left-behind step's surroundings settings; the defaults when null.</param>
    public static Settings CreateSettings(FollowUpRemovalMode mode, VisibleSurroundingObjectsSettings? surroundings = null)
    {
        var settings = new Settings
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
                ShrinkOrTurn = SettingsVariants.NothingShrunkOrTurned,
                FollowUpRemoval = new FollowUpRemovalSettings { Mode = mode },
            },
        };
        if (surroundings is not null) settings.Standard.LeftoverVisibleObjects = new LeftoverVisibleObjectSettings { SurroundingObjects = surroundings };
        return settings;
    }

    /// <param name="addObjects">Adds the objects of the test's own to the ring world's mods.</param>
    public static IReadOnlyList<ISkyrimMod> Create(string dataFolder, Action<RingWorldMods> addObjects)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod(BaseGamePlugin);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        var mods = new RingWorldMods(
            baseGame, Create(spaces, FixtureWorld.TargetName, baseGame), Create(spaces, TrustedPlugin, baseGame), Create(spaces, UntrustedPlugin, baseGame));

        baseGame.Interior.Place("Sky_Floor", baseGame.AddModeledStatic("Sky_FloorBase", FixtureMeshes.BuildingModel), new Vector3(0f, 0f, FloorTop - FixtureMeshes.BuildingHeight));
        var crate = mods.Target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        var cause = mods.Untrusted.AddModeledStatic("U_CauseBase", FixtureMeshes.PadModel);
        mods.Target.Interior.Place(MiddleCrate, crate, Vector3.Zero);
        for (var ring = 0; ring < RingCrates; ring++)
        {
            var angle = (FirstRingAngle + ring * AngleStep) * MathF.PI / 180f;
            var position = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * RingDistance;
            mods.Target.Interior.Place($"T_Ring{ring}", crate, position);
            mods.Untrusted.Interior.Place($"U_Cause{ring}", cause, position + new Vector3(CauseShift, 0f, 0f));
        }

        addObjects(mods);
        return [baseGame.Mod, mods.Target.Mod, mods.Trusted.Mod, mods.Untrusted.Mod];
    }

    public static TargetId IdOf(RunOutcome outcome, string editorId) => outcome.Collected.Targets.Single(target => target.EditorId == editorId).Id;

    /// <summary>A door of the cleaned mod rests on the middle crate; a pad of a trusted mod rests on the door.</summary>
    public static void AddDoorWithPadOnIt(RingWorldMods mods)
    {
        mods.Target.Interior.Place(DoorEditorId, mods.Target.AddModeledDoor("T_DoorBase", FixtureMeshes.CrateModel), new Vector3(0f, 0f, FixtureMeshes.CrateSize));
        mods.Trusted.Interior.Place(PadEditorId, mods.Trusted.AddModeledStatic("Trusted_PadBase", FixtureMeshes.PadModel), new Vector3(0f, 0f, 2 * FixtureMeshes.CrateSize));
    }

    private static FixtureMod Create(FixtureSpaces spaces, string plugin, FixtureMod baseGame)
    {
        var mod = new FixtureMod(plugin, baseGame);
        spaces.AddOverridesTo(mod);
        return mod;
    }
}
