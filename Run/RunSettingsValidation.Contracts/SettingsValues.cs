using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover;

/// <summary>
/// The top level of the user's settings: which mode runs, whether every run writes the detailed log and the report files,
/// whether a mod that requires the cleaned mod and loads after the patch only warns instead of stopping the run,
/// the one mod's settings of Standard mode, and what Batch mode reads.
/// </summary>
public interface IModeSettingsValues
{
    PatchMode Mode { get; }
    bool DetailedLogAndReports { get; }
    bool OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch { get; }
    ISettingsValues Standard { get; }
    string? GroupFileName { get; }
}

/// <summary>
/// The user's settings as validation reads them. The entry's settings classes implement these, so settings.json
/// keys stay with the classes; a section or list that settings.json left empty is given here as its default.
/// </summary>
public interface ISettingsValues
{
    string? CleanedMod { get; }
    IChangedObjectsSettingsValues ChangedObjects { get; }
    ITrustSettingsValues Trust { get; }
    ICollisionSettingsValues Collision { get; }
    IFollowUpSettingsValues FollowUpRemoval { get; }
    IVisibleLeftoverSettingsValues LeftoverVisibleObjects { get; }
    ILeftoverSettingsValues LeftoverInvisibleObjects { get; }
}

/// <summary>How other mods' objects the cleaned mod changed are checked, and what happens when one would be removed.</summary>
public interface IChangedObjectsSettingsValues
{
    IMovedObjectsSettingsValues Moved { get; }
    IReplacedObjectsSettingsValues Replaced { get; }
    ICollisionSettingsValues Collision { get; }
}

/// <summary>What both kinds of changed object have in common.</summary>
public interface IChangedKindSettingsValues
{
    RestoreMode WhenRemoved { get; }
    bool AttemptResizingBeforeRemoval { get; }
}

public interface IMovedObjectsSettingsValues : IChangedKindSettingsValues
{
    float MovedDistanceInUnits { get; }
}

public interface IReplacedObjectsSettingsValues : IChangedKindSettingsValues
{
    bool Process { get; }
}

/// <summary>The groups that decide whether an object clashes where it stands, and what may be done to it before it is removed.</summary>
public interface ICollisionSettingsValues
{
    ITooCloseSettingsValues TooClose { get; }
    ITerrainChangeSettingsValues TerrainChange { get; }
    IShrinkOrTurnSettingsValues ShrinkOrTurn { get; }
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
    bool CountWaterHeightChanges { get; }
    float WaterHeightChangeTolerance { get; }
    bool CountWaterAddedOrRemoved { get; }
    int MinWaterCoverChangePercent { get; }
}

public interface IShrinkOrTurnSettingsValues
{
    int ShrinkLimitPercent { get; }
    int TurnLimitDegrees { get; }
}

/// <summary>The three lists hold the plugin names as the settings spell them; an unset entry is left out.</summary>
public interface ITrustSettingsValues
{
    IReadOnlyList<string> TrustedModsAndTheirMasters { get; }
    IReadOnlyList<string> ModsTrustedForOwnChangesOnly { get; }
    IReadOnlyList<string> NeverTrustedMods { get; }
    bool TrustModsRequiringCleanedModAndTheirMasters { get; }
    int MaxOtherMastersOfRequiringModToTrust { get; }
    bool TrustModsLoadingBeforeCleanedMod { get; }
}

public interface IFollowUpSettingsValues
{
    FollowUpRemovalMode Mode { get; }
    float TouchDistance { get; }
    float AnchoringThresholdPercent { get; }
    OtherModsObjectsHandling OtherModsObjectsLeftWithoutSupport { get; }
}

/// <summary>How the cleaned mod's visible objects around an object decide whether it is left behind; both left-behind steps read it.</summary>
public interface ISurroundingObjectsSettingsValues
{
    float SearchRadius { get; }
    int MinOccupiedDirections { get; }
    int DirectionThresholdPercent { get; }
    int ClearedDirectionsPercent { get; }
}

public interface IVisibleLeftoverSettingsValues
{
    bool RemoveLeftoverVisibleObjects { get; }
    ISurroundingObjectsSettingsValues SurroundingObjects { get; }
    IReadOnlyList<VisibleObjectKind> NeverRemove { get; }
}

public interface ILeftoverSettingsValues
{
    bool RemoveLeftoverInvisibleObjects { get; }
    ISurroundingObjectsSettingsValues SurroundingObjects { get; }
    IReadOnlyList<InvisibleObjectKind> NeverRemove { get; }
    bool MoveKeptMarkersOutOfOtherModsObjects { get; }
}
