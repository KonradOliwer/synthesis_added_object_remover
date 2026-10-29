using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Run.RunSettingsValidation.Contracts;

/// <summary>The validated settings of one run.</summary>
/// <param name="Target">The target plugin as the load order spells it; used for comparisons.</param>
/// <param name="TargetAsTyped">The target plugin as the settings spell it; printed in the log and reports.</param>
/// <param name="ChangedObjects">What happens to another mod's object the cleaned mod changed when a removal step would remove it.</param>
/// <param name="ChangedObjectCollision">How another mod's object the cleaned mod changed is checked for a clash, where the cleaned mod put it and where it stood before.</param>
/// <param name="VisibleLeftBehind">Null when the left-behind visible objects step is off.</param>
/// <param name="LeftBehind">Null when the left-behind invisible objects step is off.</param>
/// <param name="MarkerMoves">Null unless kept markers are moved, which needs the invisible left-behind step.</param>
/// <param name="Reports">Null when the detailed log and reports are off.</param>
/// <param name="OnlyWarnWhenRequiringModLoadsAfterPatch">Whether a mod that requires the cleaned mod and loads after the patch is a warning instead of a stop.</param>
public sealed record RunSettings(
    PluginName Target,
    PluginName TargetAsTyped,
    ModTrustSettings ModTrust,
    WhatToCollect Read,
    SelectionSettings Selection,
    CollisionSettings Collision,
    ChangedObjectSettings ChangedObjects,
    ChangedObjectCollision ChangedObjectCollision,
    AlsoRemoveSettings AlsoRemove,
    OtherModsObjectsSettings OtherModsObjects,
    VisibleLeftBehindSettings? VisibleLeftBehind,
    LeftBehindSettings? LeftBehind,
    MarkerMoveSettings? MarkerMoves,
    ReportFileSettings? Reports,
    bool OnlyWarnWhenRequiringModLoadsAfterPatch,
    Execution Execution)
{
    /// <summary>The only-warn switch's name on the settings screen.</summary>
    public const string OnlyWarnSettingName = "Only warn (don't stop) when a mod that requires the cleaned mod loads after the patch";

    /// <summary>The one "Detailed log and reports" setting: it turns on the detailed log and the report files together.</summary>
    public bool DetailedLog => Reports is not null;

    /// <summary>One group per origin that has target objects, in the order of <see cref="TargetOrigin"/>, each judged with the collision settings of its origin.</summary>
    public ImmutableArray<CollisionGroup> CollisionGroupsOf(ImmutableArray<TargetObject> targets) =>
    [
        .. Enum.GetValues<TargetOrigin>()
            .Select(origin => new CollisionGroup([.. targets.Where(target => target.Origin == origin).Select(target => target.Id)], CollisionOf(origin)))
            .Where(group => !group.Objects.IsEmpty),
    ];

    private CollisionSettings CollisionOf(TargetOrigin origin) => origin switch
    {
        TargetOrigin.CleanedMod => Collision,
        TargetOrigin.Moved => ChangedObjectCollision.ForMoved(),
        TargetOrigin.Replaced => ChangedObjectCollision.ForReplaced(),
        _ => throw new UnreachableException($"Unknown target origin {origin}."),
    };
}
