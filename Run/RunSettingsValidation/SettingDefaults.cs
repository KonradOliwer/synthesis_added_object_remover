using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover;

/// <summary>The value each setting has until the user changes it; also what an invalid value is replaced with.</summary>
public static class SettingDefaults
{
    public const bool RemoveTooCloseBySize = true;
    public const float SizeMultiplier = 0.5f;
    public const bool RemoveTooCloseByDistance = false;
    public const float RemovalDistanceInUnits = 32f;
    public const bool UseRoughBoxes = false;
    public const bool RemoveOnChangedTerrain = false;
    public const TerrainCheckedObjects TerrainChecked = TerrainCheckedObjects.VisibleObjectsOnTheGround;
    public const bool TerrainAreaBySize = false;
    public const bool TerrainAreaByDistance = true;
    public const float TerrainAreaDistanceInUnits = 64f;
    public const bool CountHeightChanges = true;
    public const float HeightChangeTolerance = 16f;
    public const bool CountTerrainTypeChanges = true;
    public const int MinTerrainTypeChangePercent = 20;
    public const int MaxOtherMastersForPatch = 10;
    public const NpcHandling Npcs = NpcHandling.OnlyWhenStuckInsideTheObject;
    public const FollowUpRemovalMode FollowUpMode = FollowUpRemovalMode.EverythingTouchingIt;
    public const float TouchDistance = 8f;
    public const float AnchoringThresholdPercent = 50f;
    public const float SearchRadius = 1024f;
    public const int DirectionThresholdPercent = 50;
    public const int RemovedDirectionsPercent = 60;
    public const int OccupiedDirectionsPercent = 50;
}
