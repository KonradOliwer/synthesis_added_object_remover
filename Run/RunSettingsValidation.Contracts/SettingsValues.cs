using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover;

/// <summary>The top level of the user's settings: which mode runs, the one mod's settings of Standard mode, and what Batch mode reads.</summary>
public interface IModeSettingsValues
{
    PatchMode Mode { get; }
    ISettingsValues Standard { get; }
    string? GroupFileName { get; }
}

/// <summary>
/// The user's settings as validation reads them. The entry's settings classes implement these, so settings.json
/// keys stay with the classes; a section or list that settings.json left empty is given here as its default.
/// </summary>
public interface ISettingsValues
{
    string? TargetPlugin { get; }
    IIgnoreSettingsValues WhatToIgnore { get; }
    ITooCloseSettingsValues TooClose { get; }
    ITerrainChangeSettingsValues TerrainChange { get; }
    IFollowUpSettingsValues FollowUpRemoval { get; }
    ILeftoverSettingsValues LeftoverInvisibleObjects { get; }
    IDiagnosticsSettingsValues Diagnostics { get; }
}

public interface ITooCloseSettingsValues
{
    bool BySize { get; }
    float SizeMultiplier { get; }
    bool ByDistance { get; }
    float DistanceInUnits { get; }
    bool UseRoughBoxes { get; }
    NpcHandling Npcs { get; }
}

public interface ITerrainChangeSettingsValues
{
    bool RemoveOnChangedTerrain { get; }
    TerrainCheckedObjects TerrainCheckedObjects { get; }
    bool AreaBySize { get; }
    float AreaSizeMultiplier { get; }
    bool AreaByDistance { get; }
    float AreaDistanceInUnits { get; }
    bool CountHeightChanges { get; }
    float HeightChangeTolerance { get; }
    bool CountTerrainTypeChanges { get; }
    int MinTerrainTypeChangePercent { get; }
}

public interface IIgnoreSettingsValues
{
    IReadOnlyList<string> ExcludedPlugins { get; }
    bool IgnoreTargetMasters { get; }
    bool IgnoreModsPatchedWithTarget { get; }
    int MaxOtherMastersForPatch { get; }
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
    IReadOnlyList<InvisibleObjectKind> NeverRemove { get; }
    bool MoveKeptMarkersOutOfOtherModsObjects { get; }
}

public interface IDiagnosticsSettingsValues
{
    bool DetailedLog { get; }
    bool WriteReportFiles { get; }
    string? ReportFolder { get; }
}
