using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The settings the end-to-end runs use; all but the normal-log variant write the detailed log and the report files.</summary>
internal static class SettingsVariants
{
    public const string TouchWithLeftBehindAndMarkerMoves = "touch-leftovers-relocation";
    public const string Support = "support";
    public const string NothingWithBoxZoneAndNpcsAsObjects = "nothing-box-npcs-as-objects";
    public const string TouchWithLeftBehindAndMarkerMovesNormalLog = "touch-leftovers-relocation-normal";

    public static readonly IReadOnlyList<string> Names =
        [TouchWithLeftBehindAndMarkerMoves, Support, NothingWithBoxZoneAndNpcsAsObjects, TouchWithLeftBehindAndMarkerMovesNormalLog];

    public static Settings Of(string name) => new()
    {
        DetailedLogAndReports = name != TouchWithLeftBehindAndMarkerMovesNormalLog,
        Standard = StandardOf(name),
    };

    private static StandardSettings StandardOf(string name) => name switch
    {
        TouchWithLeftBehindAndMarkerMoves => Create(
            useRoughBoxes: false, NpcHandling.OnlyWhenStuckInsideTheObject, FollowUpRemovalMode.EverythingTouchingIt,
            new LeftoverInvisibleObjectSettings
            {
                RemoveLeftoverInvisibleObjects = true,
                MoveKeptMarkersOutOfOtherModsObjects = true,
                AlwaysKeep = new KeptInvisibleObjectSettings { SoundMarkers = true },
            }),
        Support => Create(
            useRoughBoxes: false, NpcHandling.OnlyWhenStuckInsideTheObject, FollowUpRemovalMode.OnlyWhatLosesItsSupport,
            new LeftoverInvisibleObjectSettings { RemoveLeftoverInvisibleObjects = true, MoveKeptMarkersOutOfOtherModsObjects = false }),
        NothingWithBoxZoneAndNpcsAsObjects => Create(
            useRoughBoxes: true, NpcHandling.AlwaysLikeAnyObject, FollowUpRemovalMode.Nothing,
            new LeftoverInvisibleObjectSettings { RemoveLeftoverInvisibleObjects = false, MoveKeptMarkersOutOfOtherModsObjects = false }),
        TouchWithLeftBehindAndMarkerMovesNormalLog => StandardOf(TouchWithLeftBehindAndMarkerMoves),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown settings variant."),
    };

    private static StandardSettings Create(bool useRoughBoxes, NpcHandling npcs, FollowUpRemovalMode followUp, LeftoverInvisibleObjectSettings leftovers) => new()
    {
        CleanedMod = ModKey.FromNameAndExtension(FixtureWorld.TargetName),
        TooClose = new TooCloseRemovalSettings
        {
            ByDistance = new TooCloseByDistanceSettings { Use = false },
            UseRoughBoxes = useRoughBoxes,
            NpcHandling = npcs,
        },
        TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = false },
        Trust = new TrustSettings
        {
            TrustedModsAndTheirMasters = [ModKey.FromNameAndExtension(FixtureWorld.TrustedPlugin)],
            NeverTrustedMods = [ModKey.FromNameAndExtension(FixtureWorld.OtherModPlugin)],
        },
        FollowUpRemoval = new FollowUpRemovalSettings { Mode = followUp },
        LeftoverInvisibleObjects = leftovers,
    };
}
