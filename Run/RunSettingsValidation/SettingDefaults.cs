using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover;

/// <summary>The value each setting has until the user changes it; also what an invalid value is replaced with.</summary>
public static class SettingDefaults
{
    public const bool RemoveTooCloseBySize = true;
    public const float SizeMultiplier = 0.5f;
    public const bool RemoveTooCloseByDistance = true;
    public const float RemovalDistanceInUnits = 50f;
    public const bool UseRoughBoxes = false;
    public const bool RemoveOnChangedTerrain = true;
    public const TerrainCheckedObjects TerrainChecked = TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes;
    public const bool TerrainAreaBySize = true;
    public const bool TerrainAreaByDistance = true;
    public const float TerrainAreaDistanceInUnits = 64f;
    public const bool CountHeightChanges = true;
    public const float HeightChangeTolerance = 16f;
    public const bool CountTerrainTypeChanges = true;
    public const int MinTerrainTypeChangePercent = 20;
    public const bool CountWaterHeightChanges = true;
    public const float WaterHeightChangeTolerance = 16f;
    public const bool CountWaterAddedOrRemoved = true;
    public const int MinWaterCoverChangePercent = 20;
    public const float MovedDistanceInUnits = 10f;
    public const RestoreMode MovedObjectsWhenRemoved = RestoreMode.RestoreIfNoCollision;
    public const bool ResizeMovedObjects = false;
    public const bool ProcessReplacedObjects = false;
    public const RestoreMode ReplacedObjectsWhenRemoved = RestoreMode.RestoreIfNoCollision;
    public const bool ResizeReplacedObjects = false;
    public const bool ShrinkOrTurnObjects = true;
    public const int ShrinkLimitPercent = 80;
    public const int TurnLimitDegrees = 360;
    public const int MaxOtherMastersOfRequiringModToTrust = 5;
    public const NpcHandling Npcs = NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision;
    public const FollowUpRemovalMode FollowUpMode = FollowUpRemovalMode.OnlyWhatLosesItsSupport;
    public const float TouchDistance = 8f;
    public const float AnchoringThresholdPercent = 50f;
    public const OtherModsObjectsHandling OtherModsObjects = OtherModsObjectsHandling.PullObjectToTheGround;
    public const float SearchRadius = 1024f;
    public const int DirectionThresholdPercent = 50;
    public const int ClearedDirectionsPercent = 60;
    public const int MinOccupiedDirections = 4;
    public const bool RemoveLeftoverVisibleObjects = true;
    public const int VisibleMinOccupiedDirections = 6;
    public const int VisibleDirectionThresholdPercent = 80;
    public const int VisibleClearedDirectionsPercent = 80;
}
