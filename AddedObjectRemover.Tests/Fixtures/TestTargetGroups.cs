using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>What the removal steps judge each target with, as the restore step reads it, for scenes that do not run those steps.</summary>
internal static class TestTargetGroups
{
    /// <summary>
    /// No target is judged by any group, so no plugin's trust is asked, no left-behind step measures neighbours, and the cleaned mod's
    /// own objects are judged by a too-close step that is off and a changed-terrain check that finds nothing, with its step off.
    /// </summary>
    public static WhereRemovedRestorer.RemovalStepJudging None =>
        new([], [], new AcceptedWorld(TestTargets.PatchName, ImmutableDictionary<PluginName, VersionTrust>.Empty), [], new(TooCloseOff, FindsNothing.Rule, ChangedTerrainOff));

    public static TooCloseSettings TooCloseOff { get; } = new(BySize: false, Multiplier: 0f, ByDistance: false, DistanceInUnits: 0f, ZoneShape.BoundingBox, NpcHandling.Never);

    public static ChangedTerrainSettings ChangedTerrainOff { get; } = new(
        On: false, TerrainCheckedObjects.VisibleObjectsOnTheGround, CountHeightChanges: false, HeightTolerance: 0f, CountTerrainTypeChanges: false, MinTerrainTypeChangePercent: 0,
        CountWaterHeightChanges: false, WaterHeightTolerance: 0f, CountWaterAddedOrRemoved: false, MinWaterCoverChangePercent: 0, AreaBySize: false, AreaMultiplier: 0f, AreaByDistance: false, AreaDistanceInUnits: 0f);

    /// <summary>A placement rule that finds every placement free.</summary>
    private sealed class FindsNothing : IPlacementRule
    {
        public static FindsNothing Rule { get; } = new();

        public IPlacementTries StartTries(TargetObject target) => new Tries();

        private sealed class Tries : IPlacementTries
        {
            public PlacementCheck Check(EulerPlacement placement) => default;

            public void Dispose()
            {
            }
        }
    }
}
