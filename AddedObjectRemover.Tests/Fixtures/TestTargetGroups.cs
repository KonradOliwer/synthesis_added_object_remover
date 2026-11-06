using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>What the removal steps judge each target with, as the restore step reads it, for scenes that do not run those steps.</summary>
internal static class TestTargetGroups
{
    /// <summary>
    /// No target is judged by any group, so no plugin's trust is asked, no left-behind step measures neighbours, and the cleaned mod's
    /// own objects are judged by a too-close step and a changed-terrain step that are off, with a changed-terrain rule that finds nothing.
    /// </summary>
    public static WhereRemovedRestorer.RemovalStepJudging None =>
        new(
            ImmutableDictionary<TargetId, ChangedTerrainSettings>.Empty,
            ImmutableDictionary<TargetId, TooCloseSettings>.Empty,
            new AcceptedWorld(TestTargets.PatchName, ImmutableDictionary<PluginName, VersionTrust>.Empty), [], new(TooCloseOff, ChangedTerrainOff), TestTerrainJudging.FindsNothing);

    /// <summary>The settings one group judges all of the targets with.</summary>
    public static ImmutableDictionary<TargetId, TSettings> SettingsOf<TSettings>(IEnumerable<TargetId> targets, TSettings settings)
        where TSettings : notnull =>
        targets.ToImmutableDictionary(target => target, _ => settings);

    public static TooCloseSettings TooCloseOff { get; } = new(BySize: false, Multiplier: 0f, ByDistance: false, DistanceInUnits: 0f, ZoneShape.BoundingBox, NpcHandling.Never);

    public static ChangedTerrainSettings ChangedTerrainOff { get; } = new(
        On: false, TerrainCheckedObjects.VisibleObjectsOnTheGround, CountHeightChanges: false, HeightTolerance: 0f, CountTerrainTypeChanges: false, MinTerrainTypeChangePercent: 0,
        CountWaterHeightChanges: false, WaterHeightTolerance: 0f, CountWaterAddedOrRemoved: false, MinWaterCoverChangePercent: 0, AreaBySize: false, AreaMultiplier: 0f, AreaByDistance: false, AreaDistanceInUnits: 0f);
}
