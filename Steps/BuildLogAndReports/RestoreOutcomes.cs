using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>A navmesh the step judged and the group it goes back or stays with; the group is null for a navmesh that is no candidate.</summary>
internal sealed record NavmeshOutcome(NavmeshChangeJudged Judged, NavmeshGroupJudged? Group)
{
    public bool IsRestored => Group is { Write: not null };
}

/// <summary>What became of each change the step judged, as the log and the report files both say it.</summary>
internal static class RestoreOutcomes
{
    /// <summary>Names a record as "EditorId [key]" when it is a placed object of the world with an editor ID, else by its key.</summary>
    public static Func<RecordKey, string> LabelsIn(CollectedObjects world)
    {
        var editorIds = world.Targets.Select(target => (target.Key, target.EditorId))
            .Concat(world.OtherModAndSupportOnlyObjects.Select(other => (other.Key, other.EditorId)))
            .Where(placed => placed.EditorId is not null)
            .ToDictionary(placed => placed.Key, placed => placed.EditorId);
        return key => RecordLabels.Of(key, editorIds.GetValueOrDefault(key));
    }

    /// <returns>Why the record stays as the cleaned mod changed it; null when it goes back.</returns>
    /// <param name="label">Names the record a guard found.</param>
    public static string? KeptBecause(PlacedChangeJudged judged, Func<RecordKey, string> label)
    {
        if (judged.IsRestored) return null;
        if (judged.Never is { } never) return RestoreWhereRemovedText.Never(never, judged.Change.WinningMod);
        if (judged.Refused is { } refusal) return RestoreWhereRemovedText.Refusal(refusal, label);
        return judged.KeptForArea is { } verdict
            ? RestoreWhereRemovedText.Area(verdict)
            : throw new UnreachableException($"The placed record {judged.Change.Key} stays with no reason given.");
    }

    /// <summary>The record goes back and the patch adds a copy of it where the cleaned mod placed it.</summary>
    public static bool GoesBackWithCopy(PlacedChangeJudged judged) => judged.IsRestored && judged.Cloning is { RefusedBy: null };

    /// <returns>The copy rule's outcome of a record that goes back without a copy because the copy is refused; null for any other record.</returns>
    public static CloneJudged? RefusedCopy(PlacedChangeJudged judged) => judged is { IsRestored: true, Cloning: { RefusedBy: not null } cloning } ? cloning : null;

    /// <returns>The parts of the cell that go back, in the order of the cell's parts, a region part once.</returns>
    public static ImmutableArray<CellPart> UndoneParts(CellChangeJudged judged) => [.. judged.Undone.Select(part => part.Name.Part).Distinct()];

    /// <returns>The parts of the cell that a later plugin changed again, each with that plugin, in the order of the cell's parts.</returns>
    public static IEnumerable<(string Part, PluginName Plugin)> LeftToLaterPlugins(CellChangedByCleanedMod change) =>
        change.Parts
            .Where(part => part.Outcome is PartOutcome.LeftToLaterPlugin)
            .Select(part => (RestoreWhereRemovedText.PartOfCell(part.Name.Part), ((PartOutcome.LeftToLaterPlugin)part.Outcome).Plugin))
            .Distinct();

    /// <returns>Why the cell keeps what the cleaned mod changed; null when something of it goes back.</returns>
    /// <param name="label">Names the record that keeps the water level.</param>
    public static string? KeptBecause(CellChangeJudged judged, Func<RecordKey, string> label)
    {
        if (judged.IsRestored) return null;
        if (judged.Never is { } never) return RestoreWhereRemovedText.Never(never, judged.Change.WinningMod);
        if (judged.KeptForArea is { } verdict) return RestoreWhereRemovedText.Area(verdict);
        return WhyWaterIsKept(judged, label) ?? throw new UnreachableException($"The cell {judged.Change.Cell} stays with no reason given.");
    }

    /// <returns>Why the cell keeps the cleaned mod's water level; null when it does not.</returns>
    /// <param name="label">Names the record that keeps the water level.</param>
    public static string? WhyWaterIsKept(CellChangeJudged judged, Func<RecordKey, string> label) =>
        judged.WaterKeptBy is { } by ? RestoreWhereRemovedText.WaterKept(judged.WaterKeptFor, by, label) : null;

    /// <returns>Why the quarter keeps what the cleaned mod changed in it; null when something of it goes back.</returns>
    /// <param name="label">Names the record a quarter held for an earlier round would undo.</param>
    public static string? KeptBecause(LandChangeJudged land, LandQuarterOutcome quarter, Func<RecordKey, string> label)
    {
        if (quarter.Restored.Any) return null;
        if (NeverHeldOrArea(land.Never, land.Change.WinningMod, quarter.HeldForEarlierRound, quarter.KeptForArea, label) is { } kept) return kept;
        List<string> reasons = [.. quarter.Kept.Select(entry => RestoreWhereRemovedText.TerrainKept(entry.Reason))];
        if (quarter.TexturesKeptForObjectThatStays) reasons.Add(RestoreWhereRemovedText.TexturesKeptForObjectThatStays);
        if (reasons.Count > 0) return string.Join(Describe.ListSeparator, reasons);
        return quarter.ColoursSharedWithGroundThatStays
            ? RestoreWhereRemovedText.ColoursSharedWithGroundThatStays
            : throw new UnreachableException($"A quarter of the land {land.Change.LandKey} stays with no reason given.");
    }

    /// <summary>The only part of the land the cleaned mod last changed that could go back is the rest of the record.</summary>
    public static bool HoldsOnlyTheRest(LandChangeJudged land) => land.Change.UndoRest && land.ChangedQuarters.IsEmpty;

    /// <returns>Why the land keeps the rest of the record the cleaned mod changed; null when it goes back.</returns>
    /// <param name="label">Names the record a quarter held for an earlier round would undo.</param>
    public static string? KeptBecauseOfRest(LandChangeJudged land, Func<RecordKey, string> label) =>
        land.Rest
            ? null
            : NeverHeldOrArea(land.Never, land.Change.WinningMod, land.RestHeldForEarlierRound, land.RestKeptForArea, label)
                ?? throw new UnreachableException($"The rest of the land record {land.Change.LandKey} stays with no reason given.");

    /// <returns>The first of a never-rule, a hold for an earlier round and an area verdict that keeps a change; null when none does.</returns>
    private static string? NeverHeldOrArea(NeverRestoredReason? never, PluginName winningMod, EarlierRoundHold? heldFor, AreaVerdict? keptForArea, Func<RecordKey, string> label) =>
        never is { } reason ? RestoreWhereRemovedText.Never(reason, winningMod)
        : heldFor is { } hold ? RestoreWhereRemovedText.HeldForEarlierRound(hold, label)
        : keptForArea is { } verdict ? RestoreWhereRemovedText.Area(verdict)
        : null;

    public static ImmutableArray<NavmeshOutcome> NavmeshOutcomes(RestoreWhereRemovedResult result)
    {
        var groupOf = result.NavmeshGroups.SelectMany(group => group.Members.Select(member => (Member: member, Group: group))).ToDictionary(entry => entry.Member, entry => entry.Group);
        return [.. result.Navmeshes.Select(navmesh => new NavmeshOutcome(navmesh, groupOf.GetValueOrDefault(navmesh.Change.Navmesh)))];
    }

    /// <returns>The parts of the navmesh that go back to what they were, in the order shape, existence, map entry.</returns>
    public static IEnumerable<string> UndoneParts(NavmeshChangedByCleanedMod change) =>
        NavmeshParts(change).Where(part => part.Outcome is PartOutcome.Undo).Select(part => part.Name);

    /// <returns>The parts of the navmesh that a later plugin changed again, each with that plugin.</returns>
    public static IEnumerable<(string Part, PluginName Plugin)> LeftToLaterPlugins(NavmeshChangedByCleanedMod change) =>
        NavmeshParts(change)
            .Where(part => part.Outcome is PartOutcome.LeftToLaterPlugin)
            .Select(part => (part.Name, ((PartOutcome.LeftToLaterPlugin)part.Outcome).Plugin));

    /// <returns>Why the navmesh keeps what the cleaned mod changed; null when it goes back.</returns>
    /// <param name="label">Names the record a guard found.</param>
    public static string? KeptBecause(NavmeshOutcome outcome, Func<RecordKey, string> label)
    {
        if (outcome.IsRestored) return null;
        var judged = outcome.Judged;
        if (outcome.Group?.Refused is { } refusal) return RestoreWhereRemovedText.Refusal(refusal, label);
        if (NeverHeldOrArea(judged.Never, judged.Change.WinningMod, judged.HeldForEarlierRound, judged.KeptForArea, label) is { } kept) return kept;
        return judged.NothingGoesBack
            ? RestoreWhereRemovedText.NothingGoesBack
            : throw new UnreachableException($"The navmesh {judged.Change.Navmesh} stays with no reason given.");
    }

    private static IEnumerable<(string Name, PartOutcome Outcome)> NavmeshParts(NavmeshChangedByCleanedMod change) =>
    [
        ("shape", change.Geometry),
        ("existence", change.Presence),
        ("map entry", change.MapEntry),
    ];
}
