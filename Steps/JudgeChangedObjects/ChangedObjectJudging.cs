using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.JudgeChangedObjects;

/// <summary>
/// Decides, before any removal round, what a round that removes a target object does with it: the cleaned mod's own objects are
/// disabled, and another mod's object the cleaned mod changed is disabled or put back to its earlier version as its mode says; it is
/// disabled also when the part that shows the change does not go back, or the restore would write it into a cell the final world
/// deletes.
/// With <see cref="RestoreMode.RestoreIfNoCollision"/> the earlier version is checked once, standing at its old place, by the
/// rules of <see cref="OldPlaceRules"/>; a failed check counts as a clash. The step removes nothing.
/// </summary>
public static class ChangedObjectJudging
{
    /// <param name="Read">What the scan collected; an old-place check needs the scan to have judged the old places.</param>
    /// <param name="ChangedTerrain">The changed-terrain rule, in the world without the cleaned mod.</param>
    public sealed record Input(
        CollectedObjects Collected,
        PluginName CleanedMod,
        WhatToCollect Read,
        IOldPlaceTooCloseRule TooClose,
        IPlacementRule ChangedTerrain,
        ChangedObjectSettings Settings,
        Execution Execution);

    /// <remarks>The old-place world is built when the first earlier version is checked, so a run whose changed objects need no check builds none.</remarks>
    public static ChangedObjectsJudged Judge(Input input)
    {
        var world = new ComputedOnce<CollectedObjects>(() => CreateOldPlaceWorld(input.Collected, input.CleanedMod, input.Read));
        var rules = new OldPlaceRules(target => input.TooClose.Create(world.Value, OldPlaceIdOf(target)), input.ChangedTerrain);
        var result = Prepare(input.Collected, rules, input.Settings, input.Execution);
        return new ChangedObjectsJudged(result, world.ValueIfComputed);
    }

    internal static ChangedObjectJudgingResult Prepare(CollectedObjects collected, OldPlaceRules rules, ChangedObjectSettings settings, Execution execution)
    {
        List<int> toCheck =
            [.. collected.Targets.Where(target => ModeOf(target, settings) == RestoreMode.RestoreIfNoCollision && RestoreCanBeWritten(target)).Select(target => target.Id.Index)];
        var checks = ParallelMap.RunOver(
            execution,
            collected.Order,
            toCheck,
            index => CheckOldPlace(collected.Targets[index], rules),
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
    internal static CollectedObjects CreateOldPlaceWorld(CollectedObjects collected, PluginName cleanedMod, WhatToCollect read)
    {
        if (!read.Needs.JudgeOldPlaces) throw new InvalidOperationException("The old-place world was asked for, but the scan did not judge the old places.");

        var cleanedModObjects = collected.Targets.Select(target => target.AsOtherObject(OldPlaceIdOf(target), cleanedMod));
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
    internal static OtherId OldPlaceIdOf(TargetObject target) => new(target.Id.Index);

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
        _ when !RestoreCanBeWritten(target) => DisableInsteadOfRestore(target),
        RestoreMode.Restore => RestoreTo(target),
        RestoreMode.RestoreIfNoCollision => checkOf[target.Id.Index] is { Collision: null, Failure: null } ? RestoreTo(target) : new WhenRemoved.Disable(),
        _ => throw new UnreachableException($"Unknown restore mode {mode}."),
    };

    private static WhenRemoved.Restore RestoreTo(TargetObject target) => new(EarlierVersionOf(target));

    /// <summary>The part that shows the cleaned mod's change goes back, and the cell the restore writes into is not deleted.</summary>
    private static bool RestoreCanBeWritten(TargetObject target) => ShownChangeGoesBack(target) && EarlierVersionOf(target).DeletedCellRestoredInto is null;

    /// <summary>
    /// Whether the part that shows the cleaned mod's change goes back: the position of a moved object, the base of a replaced one.
    /// Without it a restore would leave the object as the cleaned mod shows it. It does not go back when a later plugin or an earlier
    /// patcher changed it again, or when the earlier version stores it in another record format than the winner, which cannot be written back.
    /// </summary>
    private static bool ShownChangeGoesBack(TargetObject target) => EarlierVersionOf(target).Undo.HasFlag(ShownChangeOf(target));

    private static WhenRemoved.DisableInsteadOfRestore DisableInsteadOfRestore(TargetObject target)
    {
        var earlier = EarlierVersionOf(target);
        return ShownChangeGoesBack(target)
            ? new(ChangedLaterBy: null, earlier.DeletedCellRestoredInto)
            : new(earlier.LeftToLaterPlugins.TryGetValue(ShownChangeOf(target), out var later) ? later : null, CellDeleted: null);
    }

    private static PlacedParts ShownChangeOf(TargetObject target) => target.Origin switch
    {
        TargetOrigin.Moved => PlacedParts.Position,
        TargetOrigin.Replaced => PlacedParts.BaseAndData,
        _ => throw new UnreachableException($"Target {target.Key} of origin {target.Origin} is not a changed object."),
    };

    private static EarlierVersion EarlierVersionOf(TargetObject target) =>
        target.EarlierVersion ?? throw new InvalidOperationException($"Target {target.Key} is not a changed object, so it has no earlier version.");

    /// <remarks>The object is checked as the restore writes it: a part left to a later plugin keeps its value there too.</remarks>
    private static OldPlaceCheck CheckOldPlace(TargetObject target, OldPlaceRules rules)
    {
        var atOldPlace = target.RestoredAs(EarlierVersionOf(target));
        var check = rules.TooClose(atOldPlace).CheckWhereItStands(atOldPlace);
        if (check.Clash is null && check.Failure is null) check = rules.ChangedTerrain.CheckWhereItStands(atOldPlace);
        return new OldPlaceCheck(target.Id, check.Clash, check.Failure);
    }

    private static PartFailure FailureOf(OldPlaceCheck check, TargetObject target) =>
        new(JudgeChangedObjectsParts.OldPlace, check.Failure, new FailureSubject(target.Key.ToString(), target.EditorId, target.Id.Index));
}
