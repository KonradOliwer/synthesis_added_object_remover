namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The settings the end-to-end runs use; every variant writes report files, and all but the normal-log one log in detail.</summary>
internal static class SettingsVariants
{
    public const string TouchWithLeftoversAndRelocation = "touch-leftovers-relocation";
    public const string Support = "support";
    public const string NothingWithBoxZoneAndNpcsAsObjects = "nothing-box-npcs-as-objects";
    public const string TouchWithLeftoversAndRelocationNormalLog = "touch-leftovers-relocation-normal";

    public static readonly IReadOnlyList<string> Names =
        [TouchWithLeftoversAndRelocation, Support, NothingWithBoxZoneAndNpcsAsObjects, TouchWithLeftoversAndRelocationNormalLog];

    public static Settings Of(string name) => name switch
    {
        TouchWithLeftoversAndRelocation => Create(
            ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInObject, FollowUpRemovalMode.EverythingTouching,
            new LeftoverInvisibleObjectSettings
            {
                RemoveLeftoverInvisibleObjects = true,
                MoveKeptMarkersOutOfOtherModsObjects = true,
                ProtectedTypes = ProtectedInvisibleObjectsPreset.Custom,
                CustomProtectedTypes = [InvisibleObjectKind.SoundMarkers],
            }),
        Support => Create(
            ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInObject, FollowUpRemovalMode.ObjectsSupportedByIt,
            new LeftoverInvisibleObjectSettings { RemoveLeftoverInvisibleObjects = true }),
        NothingWithBoxZoneAndNpcsAsObjects => Create(
            ZoneShape.BoundingBox, NpcHandling.CountLikeObjects, FollowUpRemovalMode.Nothing,
            new LeftoverInvisibleObjectSettings { RemoveLeftoverInvisibleObjects = false }),
        TouchWithLeftoversAndRelocationNormalLog => WithNormalLog(Of(TouchWithLeftoversAndRelocation)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown settings variant."),
    };

    private static Settings Create(ZoneShape zone, NpcHandling npcs, FollowUpRemovalMode followUp, LeftoverInvisibleObjectSettings leftovers) => new()
    {
        WhatToCheck = new CheckSettings { TargetPlugin = FixtureWorld.TargetPlugin, ZoneShape = zone },
        WhatToIgnore = new IgnoreSettings
        {
            ExcludedPlugins = [FixtureWorld.ExcludedPlugin],
            IgnoreTargetMasters = true,
            IgnoreModsPatchedWithTarget = true,
            NpcHandling = npcs,
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
