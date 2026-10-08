using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover;

/// <summary>
/// The user's settings as validation reads them. The entry's settings classes implement these, so settings.json
/// keys stay with the classes; a section or list that settings.json left empty is given here as its default.
/// </summary>
internal interface ISettingsValues
{
    ICheckSettingsValues WhatToCheck { get; }
    IIgnoreSettingsValues WhatToIgnore { get; }
    IFollowUpSettingsValues FollowUpRemoval { get; }
    ILeftoverSettingsValues LeftoverInvisibleObjects { get; }
    IDiagnosticsSettingsValues Diagnostics { get; }
}

internal interface ICheckSettingsValues
{
    string? TargetPlugin { get; }
    float SizeMultiplier { get; }
    ZoneShape ZoneShape { get; }
}

internal interface IIgnoreSettingsValues
{
    IReadOnlyList<string?> ExcludedPlugins { get; }
    bool IgnoreTargetMasters { get; }
    bool IgnoreModsPatchedWithTarget { get; }
    int MaxOtherMastersForPatch { get; }
    NpcHandling NpcHandling { get; }
}

internal interface IFollowUpSettingsValues
{
    FollowUpRemovalMode Mode { get; }
    float TouchDistance { get; }
    float AnchoringThresholdPercent { get; }
}

internal interface ILeftoverSettingsValues
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

internal interface IDiagnosticsSettingsValues
{
    bool DetailedLog { get; }
    bool WriteReportFiles { get; }
    string? ReportFolder { get; }
}

/// <summary>The value each setting has until the user changes it; also what an invalid value is replaced with.</summary>
internal static class SettingDefaults
{
    public const float SizeMultiplier = 0.5f;
    public const ZoneShape Zone = ZoneShape.ObjectShape;
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
