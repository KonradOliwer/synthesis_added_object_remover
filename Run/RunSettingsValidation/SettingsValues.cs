using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover;

/// <summary>
/// The user's settings as validation reads them. The entry's settings classes implement these, so settings.json
/// keys stay with the classes; a section or list that settings.json left empty is given here as its default.
/// </summary>
public interface ISettingsValues
{
    ICheckSettingsValues WhatToCheck { get; }
    ITerrainChangeSettingsValues TerrainChange { get; }
    ITooCloseSettingsValues TooClose { get; }
    IIgnoreSettingsValues WhatToIgnore { get; }
    IFollowUpSettingsValues FollowUpRemoval { get; }
    ILeftoverSettingsValues LeftoverInvisibleObjects { get; }
    IDiagnosticsSettingsValues Diagnostics { get; }
}

public interface ICheckSettingsValues
{
    string? TargetPlugin { get; }
}

public interface ITooCloseSettingsValues
{
    bool RemoveTooCloseBySize { get; }
    float SizeMultiplier { get; }
    bool RemoveTooCloseByDistance { get; }
    float RemovalDistanceInUnits { get; }
    ZoneShape ZoneShape { get; }
}

public interface ITerrainChangeSettingsValues
{
    bool RemoveOnChangedTerrain { get; }
    TerrainCheckedObjects TerrainCheckedObjects { get; }
    bool CountHeightChanges { get; }
    float HeightChangeTolerance { get; }
    bool CountTerrainTypeChanges { get; }
    TerrainDistanceMode TerrainDistanceMode { get; }
    float TerrainDistanceInUnits { get; }
    float TerrainDistanceSizeMultiplier { get; }
}

public interface IIgnoreSettingsValues
{
    IReadOnlyList<string?> ExcludedPlugins { get; }
    bool IgnoreTargetMasters { get; }
    bool IgnoreModsPatchedWithTarget { get; }
    int MaxOtherMastersForPatch { get; }
    NpcHandling NpcHandling { get; }
}

public interface IFollowUpSettingsValues
{
    FollowUpRemovalMode Mode { get; }
    float TouchDistance { get; }
    float AnchoringThresholdPercent { get; }
}

public interface ILeftoverSettingsValues
{
    bool RemoveLeftoverInvisibleObjects { get; }
    float SearchRadius { get; }
    int DirectionThresholdPercent { get; }
    int RemovedDirectionsPercent { get; }
    int OccupiedDirectionsPercent { get; }
    ProtectedInvisibleObjectsPreset ProtectedTypes { get; }
    IReadOnlyList<InvisibleObjectKind> CustomProtectedTypes { get; }
    bool MoveKeptMarkersOutOfOtherModsObjects { get; }
}

public interface IDiagnosticsSettingsValues
{
    bool DetailedLog { get; }
    bool WriteReportFiles { get; }
    string? ReportFolder { get; }
}

/// <summary>The value each setting has until the user changes it; also what an invalid value is replaced with.</summary>
public static class SettingDefaults
{
    public const bool RemoveTooCloseBySize = true;
    public const float SizeMultiplier = 0.5f;
    public const bool RemoveTooCloseByDistance = false;
    public const float RemovalDistanceInUnits = 32f;
    public const ZoneShape Zone = ZoneShape.ObjectShape;
    public const bool RemoveOnChangedTerrain = false;
    public const TerrainCheckedObjects TerrainChecked = TerrainCheckedObjects.VisibleObjectsOnTheGround;
    public const bool CountHeightChanges = true;
    public const float HeightChangeTolerance = 16f;
    public const bool CountTerrainTypeChanges = true;
    public const TerrainDistanceMode TerrainDistance = TerrainDistanceMode.GameUnits;
    public const float TerrainDistanceInUnits = 64f;
    public const float TerrainDistanceSizeMultiplier = 0.5f;
    public const int MaxOtherMastersForPatch = 10;
    public const NpcHandling Npcs = NpcHandling.OnlyWhenStuckInObject;
    public const FollowUpRemovalMode FollowUpMode = FollowUpRemovalMode.EverythingTouching;
    public const float TouchDistance = 8f;
    public const float AnchoringThresholdPercent = 50f;
    public const float SearchRadius = 1024f;
    public const int DirectionThresholdPercent = 50;
    public const int RemovedDirectionsPercent = 60;
    public const int OccupiedDirectionsPercent = 50;
    public const string ReportFolder = "AddedObjectRemover Reports";
}
