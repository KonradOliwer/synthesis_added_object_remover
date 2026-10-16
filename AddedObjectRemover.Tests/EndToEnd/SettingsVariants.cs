using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The settings the end-to-end runs use; every variant writes report files, and all but the normal-log one log in detail.</summary>
internal static class SettingsVariants
{
    public const string TouchWithLeftBehindAndMarkerMoves = "touch-leftovers-relocation";
    public const string Support = "support";
    public const string NothingWithBoxZoneAndNpcsAsObjects = "nothing-box-npcs-as-objects";
    public const string TouchWithLeftBehindAndMarkerMovesNormalLog = "touch-leftovers-relocation-normal";

    public static readonly IReadOnlyList<string> Names =
        [TouchWithLeftBehindAndMarkerMoves, Support, NothingWithBoxZoneAndNpcsAsObjects, TouchWithLeftBehindAndMarkerMovesNormalLog];

    public static Settings Of(string name) => name switch
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
            new LeftoverInvisibleObjectSettings { RemoveLeftoverInvisibleObjects = true }),
        NothingWithBoxZoneAndNpcsAsObjects => Create(
            useRoughBoxes: true, NpcHandling.AlwaysLikeAnyObject, FollowUpRemovalMode.Nothing,
            new LeftoverInvisibleObjectSettings { RemoveLeftoverInvisibleObjects = false }),
        TouchWithLeftBehindAndMarkerMovesNormalLog => WithNormalLog(Of(TouchWithLeftBehindAndMarkerMoves)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown settings variant."),
    };

    private static Settings Create(bool useRoughBoxes, NpcHandling npcs, FollowUpRemovalMode followUp, LeftoverInvisibleObjectSettings leftovers) => new()
    {
        TargetPlugin = ModKey.FromNameAndExtension(FixtureWorld.TargetPlugin),
        TooClose = new TooCloseRemovalSettings { UseRoughBoxes = useRoughBoxes, NpcHandling = npcs },
        WhatToIgnore = new IgnoreSettings
        {
            ExcludedPlugins = [ModKey.FromNameAndExtension(FixtureWorld.ExcludedPlugin)],
            IgnoreTargetMasters = true,
            CompatibilityPatches = new CompatibilityPatchSettings { IgnoreModsPatchedWithTarget = true },
        },
        FollowUpRemoval = new FollowUpRemovalSettings { Mode = followUp },
        LeftoverInvisibleObjects = leftovers,
        Diagnostics = new DiagnosticsSettings { DetailedLog = true, WriteReportFiles = true },
    };

    private static Settings WithNormalLog(Settings settings)
    {
        settings.Diagnostics!.DetailedLog = false;
        return settings;
    }
}
