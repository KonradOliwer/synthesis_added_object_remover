using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.JudgeChangedObjects;

/// <summary>
/// Decides, before any removal round, what a round that removes a target object does with it: the cleaned mod's own objects are
/// disabled, and another mod's object the cleaned mod changed is disabled or put back to its earlier version as its mode says.
/// With <see cref="RestoreMode.RestoreIfNoCollision"/> the earlier version is checked once, standing at its old place, by the
/// rules of <see cref="OldPlaceRules"/>; a failed check counts as a clash. The step removes nothing.
/// </summary>
public static class ChangedObjectJudging
{
    public static ChangedObjectJudgingResult Prepare(CollectedObjects collected, OldPlaceRules rules, ChangedObjectSettings settings, Execution execution)
    {
        List<int> toCheck = [.. collected.Targets.Where(target => ModeOf(target, settings) == RestoreMode.RestoreIfNoCollision).Select(target => target.Id.Index)];
        var checks = ParallelMap.RunOver(
            execution,
            collected.Order,
            toCheck,
            static () => (object?)null,
            (index, _) => CheckOldPlace(collected.Targets[index], rules),
            ParallelMap.OneItemPerRange);
        var checkOf = toCheck.Zip(checks).ToDictionary(pair => pair.First, pair => pair.Second);
        return new ChangedObjectJudgingResult(
            [.. collected.Targets.Select(target => WhenRemovedOf(target, ModeOf(target, settings), checkOf))],
            [.. checks],
            [.. checks.Where(check => check.Failure is not null).Select(check => FailureOf(check, collected.Targets[check.Target.Index]))]);
    }

    /// <summary>
    /// The objects an earlier version is judged among at its old place, as a scan result with no target objects: the cleaned mod's
    /// objects where it put them (the object with <see cref="OldPlaceIdOf"/> a target's id is that target), then every other object
    /// that is a change in the world without the cleaned mod, in their id order. Everything else stood there without the cleaned mod.
    /// In this world every object's <see cref="OtherObject.Change"/> is its change without the cleaned mod.
    /// </summary>
    /// <param name="read">What the scan collected; the world needs the scan to have judged the old places.</param>
    public static CollectedObjects CreateOldPlaceWorld(CollectedObjects collected, PluginName cleanedMod, WhatToCollect read)
    {
        if (!read.JudgeOldPlaces) throw new InvalidOperationException("The old-place world was asked for, but the scan did not judge the old places.");

        var cleanedModObjects = collected.Targets.Select(target => AsOtherObject(target, cleanedMod));
        var changed = collected.OtherModAndSupportOnlyObjects
            .Where(other => other.ChangeWithoutCleanedMod is not null)
            .Select(other => other with { Change = other.ChangeWithoutCleanedMod });
        return collected with
        {
            Targets = [],
            OtherModObjects = [.. cleanedModObjects.Concat(changed).Select((other, index) => other with { Id = new OtherId(index) })],
            SupportOnlyObjects = [],
        };
    }

    /// <returns>The id, in the world of <see cref="CreateOldPlaceWorld"/>, of the cleaned mod's object that is the target.</returns>
    public static OtherId OldPlaceIdOf(TargetObject target) => new(target.Id.Index);

    private static RestoreMode ModeOf(TargetObject target, ChangedObjectSettings settings) => target.Origin switch
    {
        TargetOrigin.CleanedMod => RestoreMode.Remove,
        TargetOrigin.Moved => settings.Moved,
        TargetOrigin.Replaced => settings.Replaced,
        _ => throw new UnreachableException($"Unknown target origin {target.Origin}."),
    };

    private static WhenRemoved WhenRemovedOf(TargetObject target, RestoreMode mode, IReadOnlyDictionary<int, OldPlaceCheck> checkOf) => mode switch
    {
        RestoreMode.Remove => new WhenRemoved.Disable(),
        RestoreMode.Restore => RestoreTo(target),
        RestoreMode.RestoreIfNoCollision => checkOf[target.Id.Index] is { Collision: null, Failure: null } ? RestoreTo(target) : new WhenRemoved.Disable(),
        _ => throw new UnreachableException($"Unknown restore mode {mode}."),
    };

    private static WhenRemoved.Restore RestoreTo(TargetObject target) => new(EarlierVersionOf(target));

    private static EarlierVersion EarlierVersionOf(TargetObject target) =>
        target.EarlierVersion ?? throw new InvalidOperationException($"Target {target.Key} is not a changed object, so it has no earlier version.");

    private static OldPlaceCheck CheckOldPlace(TargetObject target, OldPlaceRules rules)
    {
        var earlier = EarlierVersionOf(target);
        var atOldPlace = target.RestoredAs(earlier);
        var check = CheckOnce(rules.TooClose(atOldPlace), atOldPlace, earlier.Placement);
        if (check.Clash is null && check.Failure is null) check = CheckOnce(rules.ChangedTerrain, atOldPlace, earlier.Placement);
        return new OldPlaceCheck(target.Id, check.Clash, check.Failure);
    }

    private static PlacementCheck CheckOnce(IPlacementRule rule, TargetObject target, EulerPlacement placement)
    {
        using var tries = rule.StartTries(target);
        return tries.Check(placement);
    }

    private static OtherObject AsOtherObject(TargetObject target, PluginName cleanedMod)
    {
        var change = new ObjectChange(cleanedMod, AcceptedFrom: null);
        return new OtherObject(
            OldPlaceIdOf(target),
            target.Key,
            target.SpaceKey,
            cleanedMod,
            target.EditorId,
            target.Base,
            target.Transform.Position,
            target.Rotation,
            target.Transform.Scale,
            target.IsPrimitive,
            target.HasMapMarker,
            change,
            ReferenceRoles.None,
            change);
    }

    private static PartFailure FailureOf(OldPlaceCheck check, TargetObject target) =>
        new(JudgeChangedObjectsParts.OldPlace, check.Failure, new FailureSubject(target.Key.ToString(), target.EditorId, target.Id.Index));
}
