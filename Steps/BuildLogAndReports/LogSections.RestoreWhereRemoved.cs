using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The step's name in the log.</summary>
    public const string RestoreWhereRemovedName = "Restore where removed";

    private const string RestoreWhereRemovedSectionId = "restore-where-removed";
    private const string CountsSeparator = ", ";
    private const string AreasSeparator = "; ";
    private const string KeptByLaterPluginsLabel = "Kept by later plugins";
    private const string FollowUpLabel = "Follow-up of what went back";

    /// <summary>
    /// What went back and what stays where the cleaned mod was removed. The normal log has the counts; the detailed log adds the
    /// counts of what stays for each reason and one line per change. A line with no fact is not printed.
    /// </summary>
    public static LogSection BuildRestoreWhereRemovedSection(CollectedObjects world, RestoreWhereRemovedResult result, TimeSpan elapsed, ReportContext context)
    {
        switch (result.Outcome)
        {
            case StepOutcome.SwitchedOff:
                return new LogSection(RestoreWhereRemovedSectionId, []);
            case StepOutcome.StoppedByUnexpectedError:
                return new LogSection(RestoreWhereRemovedSectionId, [BuildNotPerformedLine(RestoreWhereRemovedName)]);
            case StepOutcome.Performed:
                break;
            default:
                throw new UnreachableException($"Unexpected outcome {result.Outcome} of {RestoreWhereRemovedName}.");
        }

        var judged = new RestoreJudgements(result);
        return new LogSection(
            RestoreWhereRemovedSectionId,
            [
                .. TextLists.Headed("Restored where the cleaned mod was removed", RestoreCountLines.Restored(judged), CountsSeparator),
                .. TextLists.Headed("Other mods' objects put back, with a copy at the cleaned mod's place", RestoreCountLines.RestoredWithCopy(judged), CountsSeparator),
                .. TextLists.Headed(FollowUpLabel, FollowUpCounts(result.FollowUp), CountsSeparator),
                .. TextLists.Headed("Left as the cleaned mod changed them", RestoreCountLines.LeftAsChanged(judged), AreasSeparator),
                .. TextLists.Headed(
                    "Other mods' objects kept at the cleaned mod's place because they may not be copied", RestoreCountLines.KeptBecauseNotCopied(judged), CountsSeparator),
                .. context.Detailed ? RestoreWhereRemovedDetailLines(world, judged, elapsed, context) : [],
            ]);
    }

    /// <summary>The line that closes the log with what went back; nothing when nothing did.</summary>
    public static LogSection BuildRestoreWhereRemovedSummarySection(RestoreWhereRemovedResult result) =>
        new("restore-where-removed-summary", [.. TextLists.Headed("Restored where the cleaned mod was removed", RestoreCountLines.RestoredInSummary(new RestoreJudgements(result)), CountsSeparator)]);

    private static IEnumerable<string> RestoreWhereRemovedDetailLines(CollectedObjects world, RestoreJudgements judged, TimeSpan elapsed, ReportContext context)
    {
        var held = HeldByLaterPlugins.In(world);
        if (judged.IsEmpty && held.IsEmpty) return [];

        var label = RestoreOutcomes.LabelsIn(world);
        return
        [
            .. TextLists.Headed(KeptByLaterPluginsLabel, RestoreCountLines.KeptByLaterPlugins(held), CountsSeparator),
            .. TextLists.Headed("Left to later plugins in the changes judged", RestoreCountLines.LeftToLaterPlugins(judged), CountsSeparator),
            .. TextLists.Headed("Kept because the cleaned mod's objects still stand around them", RestoreCountLines.KeptForArea(judged, AreaVerdict.StillStanding), CountsSeparator),
            .. TextLists.Headed("Kept because the cleaned mod has no objects around them", RestoreCountLines.KeptForArea(judged, AreaVerdict.NoObjectsAround), CountsSeparator),
            .. TextLists.Headed("Not restored whatever the area around them", RestoreCountLines.NeverRestored(judged), CountsSeparator),
            .. judged.Result.Objects.Select(change => RestoredObjectLine(world, change, judged.FollowedAfterGoingBack, label, context)),
            .. judged.Result.FollowUp.Targets.Select(followed => FollowUpTargetLine(world, followed)),
            .. judged.Result.FollowUp.Others.Select(other => FollowUpOtherLine(world, other, context)),
            .. judged.Result.Cells.Select(cell => RestoredCellLine(world, cell, label)),
            .. judged.Result.Lands.SelectMany(land => RestoredLandLines(world, land)),
            .. judged.Navmeshes.Select(navmesh => RestoredNavmeshLine(world, navmesh, label)),
            .. held.Objects.Select(change => HeldObjectLine(world, change, context)),
            .. held.Cells.Select(cell => HeldCellLine(world, cell)),
            .. held.Lands.Select(land => HeldLandLine(world, land)),
            $"{RestoreWhereRemovedName}: {TextFormat.Count(judged.Result.Passes, "pass", "passes")}{Describe.TimedSuffix(elapsed, context)}.",
            .. TerrainFindingsLeftLines(judged.Result),
            .. RestoreWhereRemovedPartTimeLines(judged.Result.Times, context),
        ];
    }

    /// <summary>
    /// "1 object the cleaned mod added or moved removed, 2 objects of trusted mods pulled down": the checked objects removed, then those
    /// restored, which only an object of another mod the cleaned mod moved or replaced can be, then the objects of trusted mods by fate,
    /// each only when there is one.
    /// </summary>
    private static IEnumerable<string> FollowUpCounts(RestoreFollowUp followUp)
    {
        var removed = followUp.Targets.Count(followed => followed.RestoredTo is null);
        var restored = followUp.Targets.Length - removed;
        return
        [
            .. removed > 0 ? [$"{TextFormat.Count(removed, "object the cleaned mod added or moved", "objects the cleaned mod added or moved")} removed"] : Array.Empty<string>(),
            .. restored > 0 ? [$"{TextFormat.Count(restored, "object the cleaned mod moved or replaced", "objects the cleaned mod moved or replaced")} restored"] : Array.Empty<string>(),
            .. Enum.GetValues<OtherModsObjectFate>()
                .Select(fate => (Fate: fate, Count: followUp.Others.Count(other => other.Outcome.Fate == fate)))
                .Where(entry => entry.Count > 0)
                .Select(entry => $"{TextFormat.Count(entry.Count, "object of a trusted mod", "objects of trusted mods")} {OtherModsObjectText.Summary(entry.Fate)}"),
        ];
    }

    /// <summary>E.g. "  Follow-up of what went back: Pebble [key] in ...: removed, with Rock [key] going back".</summary>
    private static string FollowUpTargetLine(CollectedObjects world, FollowUpTarget followed)
    {
        var target = world.Targets[followed.Target.Index];
        var decided = followed.RestoredTo is null ? "removed" : "restored";
        return $"  {FollowUpLabel}: {RecordNames.Describe(target)} in {Describe.Location(world, target)}: {decided}{StartedByText(world, followed.StartedBy, followed.ByGround)}";
    }

    /// <summary>E.g. "  Follow-up of what went back: Lantern [key] (base ...) in ... at (1, 2, 3): pulled down by 40.0 units, with Rock [key] going back".</summary>
    private static string FollowUpOtherLine(CollectedObjects world, FollowUpOtherObject other, ReportContext context)
    {
        var outcome = other.Outcome;
        var placed = outcome.Object;
        return $"  {FollowUpLabel}: {Describe.OtherObject(placed)} (base {RecordNames.DescribeBase(context.Bases, placed.Base)}) in {Describe.Space(world, placed.SpaceKey)} "
            + $"at {DescribePosition(placed)}: {OtherModsObjectText.Outcome(outcome.Fate)}{FateDetail(outcome)}{StartedByText(world, other.StartedBy, other.ByGround)}";
    }

    /// <summary>
    /// ", with Rock [key], Rock [key] going back", ", with the ground going back down" or both: the placed records going back and the
    /// ground going back that started it.
    /// </summary>
    private static string StartedByText(CollectedObjects world, ImmutableArray<RecordKey> startedBy, bool byGround) =>
        ", with " + string.Join(
            " and ",
            new[]
            {
                startedBy.IsEmpty ? null : $"{string.Join(Describe.ListSeparator, startedBy.Select(RestoreOutcomes.LabelsIn(world)))} going back",
                byGround ? "the ground going back down" : null,
            }.OfType<string>());

    /// <summary>Nothing when there are none.</summary>
    private static IEnumerable<string> TerrainFindingsLeftLines(RestoreWhereRemovedResult result) =>
        result.TerrainFindingsLeft == 0
            ? []
            :
            [
                $"{RestoreWhereRemovedName}: {TextFormat.Count(result.TerrainFindingsLeft, "object that stays or copy", "objects that stay or copies")} on terrain the "
                + "changed-terrain check finds changed, where the restore writes nothing of what the check counts.",
            ];

    private static IEnumerable<string> RestoreWhereRemovedPartTimeLines(RestoreWhereRemovedTimes times, ReportContext context) =>
    [
        .. PartTimeLine("areas judged and plans made", times.PlansMade, context),
        .. PartTimeLine("world after removal", times.WorldAfterRemoval, context),
        .. PartTimeLine("objects that stay and guards set up, with the first collision layer read", times.GuardsSetUp, context),
        .. PartTimeLine("passes", times.Passes, context),
        .. PartTimeLine("plans read in the passes", times.InPasses.PlansRead, context),
        .. PartTimeLine("follow-up of what went back in the passes", times.InPasses.FollowUp, context),
        .. PartTimeLine("objects that stand read in the passes", times.InPasses.WhatStands, context),
        .. PartTimeLine("placed guards in the passes", times.InPasses.PlacedGuards, context),
        .. PartTimeLine("changed-terrain check in the passes", times.InPasses.ChangedTerrain, context),
        .. PartTimeLine("navmesh guards in the passes", times.InPasses.NavmeshGuards, context),
        .. PartTimeLine("ground protected in the passes", times.InPasses.GroundProtected, context),
        .. PartTimeLine("water levels kept in the passes", times.InPasses.WaterKept, context),
    ];

    private static ImmutableArray<string> PartTimeLine(string part, TimeSpan time, ReportContext context) =>
        StepTimeLines($"{RestoreWhereRemovedName}, {part}", time, context);

    /// <summary>E.g. "  Moved back: Rock [key] (base ...) in ... to (1, 2, 3); then pulled down to (1, 2, 0) by the follow-up of what went back; ...".</summary>
    /// <param name="followedAfterGoingBack">What the follow-up of what went back then does to each placed record that went back, by key.</param>
    /// <param name="label">Names a record a guard found.</param>
    private static string RestoredObjectLine(
        CollectedObjects world, PlacedChangeJudged judged, IReadOnlyDictionary<RecordKey, OtherModsObjectOutcome> followedAfterGoingBack, Func<RecordKey, string> label, ReportContext context)
    {
        var change = judged.Change;
        var record = ObjectText(world, change, context);
        var later = Tables.LeftToLaterPlugins(change.Earlier) is { } plugins ? $"; left to later plugins: {plugins}" : string.Empty;
        var areas = ObjectAreas(judged);
        if (RestoreOutcomes.KeptBecause(judged, label) is { } kept) return $"  Kept as changed: {record}: {kept}{areas}{later}";

        var copy = RestoreOutcomes.GoesBackWithCopy(judged) ? $", {RestoreWhereRemovedText.WithCopy}" : string.Empty;
        var followed = followedAfterGoingBack.TryGetValue(change.Key, out var outcome) ? $"; {RestoreWhereRemovedText.ThenFollowedUp(outcome)}" : string.Empty;
        return $"  {RestoreWhereRemovedText.KindVerb(judged.Kind)}: {record}{WhereObjectGoesBack(change, context)}{copy}{followed}{areas}{later}";
    }

    /// <summary>E.g. "Rock [key] (base Crate [key]) in worldspace Tamriel, cell (1, 2)".</summary>
    private static string ObjectText(CollectedObjects world, RecordChangedByCleanedMod change, ReportContext context)
    {
        var cell = change.Cell.Key == change.SpaceKey ? string.Empty : $", cell {Describe.Cell(change.Cell)}";
        return $"{RecordLabels.Of(change.Key, change.EditorId)} (base {RecordNames.DescribeBase(context.Bases, change.BaseNow)}) in {Describe.Space(world, change.SpaceKey)}{cell}";
    }

    /// <summary>E.g. "worldspace Tamriel, cell (1, 2)", or the interior itself.</summary>
    private static string CellPlace(CollectedObjects world, CellChangedByCleanedMod change) =>
        change.Grid is var (x, y) ? $"{Describe.Space(world, change.SpaceKey)}, cell ({x}, {y})" : Describe.Space(world, change.SpaceKey);

    private static string LandPlace(CollectedObjects world, ExteriorCell cell) => $"{Describe.Space(world, cell.WorldspaceKey)}, cell ({cell.X}, {cell.Y})";

    private static string HeldObjectLine(CollectedObjects world, RecordChangedByCleanedMod change, ReportContext context) =>
        $"  {KeptByLaterPluginsLabel}: {ObjectText(world, change, context)}: {Tables.LeftToLaterPlugins(change.Earlier)}";

    private static string HeldCellLine(CollectedObjects world, CellChangedByCleanedMod change) =>
        $"  {KeptByLaterPluginsLabel}: cell {CellPlace(world, change)}: {Describe.PartsByPlugin(RestoreOutcomes.LeftToLaterPlugins(change))}";

    private static string HeldLandLine(CollectedObjects world, LandChangedByCleanedMod land)
    {
        var place = LandPlace(world, land.Cell);
        var parts = string.Join(Describe.ListSeparator, RestoreCountLines.TerrainLeftToLater([land.LeftToLater]));
        return $"  {KeptByLaterPluginsLabel}: terrain {place}: {parts} ({string.Join(Describe.ListSeparator, land.LeftToLater.Plugins)})";
    }

    /// <summary>E.g. " to (10, 20, 30)", and " to base X" for a model that goes back.</summary>
    private static string WhereObjectGoesBack(RecordChangedByCleanedMod change, ReportContext context)
    {
        var earlier = change.Earlier;
        var position = earlier.Undo.HasFlag(PlacedParts.Position) ? $" to {RestoreWhereRemovedText.Place(earlier.Placement.Position)}" : string.Empty;
        var model = earlier.Undo.HasFlag(PlacedParts.BaseAndData) && earlier.Base != change.BaseNow ? $" to base {RecordNames.DescribeBase(context.Bases, earlier.Base)}" : string.Empty;
        return position + model;
    }

    /// <summary>"; 0 of 214 objects still stand (0.0% by size)" around the old place and, for an object the removals left in place, where it stands now.</summary>
    private static string ObjectAreas(PlacedChangeJudged judged) =>
        judged.WhereItIsNow is null
            ? ClearedAreaText.After(judged.OldPlace)
            : ClearedAreaText.After(judged.OldPlace, "at its old place ") + ClearedAreaText.After(judged.WhereItIsNow, "where it stands now ");

    /// <param name="label">Names the record that keeps the water level.</param>
    private static string RestoredCellLine(CollectedObjects world, CellChangeJudged judged, Func<RecordKey, string> label)
    {
        var change = judged.Change;
        var place = CellPlace(world, change);
        var area = ClearedAreaText.After(judged.Area);
        var later = Describe.PartsByPlugin(RestoreOutcomes.LeftToLaterPlugins(change)) is { Length: > 0 } plugins ? $"; left to later plugins: {plugins}" : string.Empty;
        if (RestoreOutcomes.KeptBecause(judged, label) is { } kept) return $"  Kept as changed: cell {place}: {kept}{area}{later}";

        var parts = string.Join(Describe.ListSeparator, RestoreOutcomes.UndoneParts(judged).Select(RestoreWhereRemovedText.PartOfCell));
        var water = RestoreOutcomes.WhyWaterIsKept(judged, label) is { } why ? $"; {RestoreWhereRemovedText.WaterLevelPart} kept: {why}" : string.Empty;
        return $"  Cell {place}: {parts}{water}{area}{later}";
    }

    private static IEnumerable<string> RestoredLandLines(CollectedObjects world, LandChangeJudged land)
    {
        var change = land.Change;
        var place = LandPlace(world, change.Cell);
        if (land.Never is { } never)
        {
            yield return $"  Kept as changed: terrain {place}: {RestoreWhereRemovedText.Never(never, change.WinningMod)}";
            yield break;
        }

        foreach (var quarter in land.ChangedQuarters)
        {
            var area = ClearedAreaText.After(quarter.Area);
            var name = $"{RestoreWhereRemovedText.Quarter(quarter.Quarter)} quarter";
            yield return RestoreOutcomes.KeptBecause(land, quarter) is { } kept
                ? $"  Kept as changed: terrain {place}, {name}: {kept}{area}"
                : $"  Terrain {place}, {name}: {RestoredTerrainPieces(quarter)}{area}";
        }
        if (land.Rest) yield return $"  Terrain {place}: {RestoreWhereRemovedText.RestOfLandRecord}";
        else if (RestoreOutcomes.HoldsOnlyTheRest(land)) yield return $"  Kept as changed: terrain {place}: {RestoreWhereRemovedText.RestOfLandRecord}: {RestoreOutcomes.KeptBecauseOfRest(land)}";
    }

    /// <summary>
    /// E.g. "210 heights; 14 heights kept under objects that stay; ground textures kept: the changed-terrain check around an object
    /// that stays ...".
    /// </summary>
    private static string RestoredTerrainPieces(LandQuarterOutcome quarter)
    {
        var kept = quarter.Kept.Select(entry => $"{RestoreWhereRemovedText.Heights(entry.Points)} kept {RestoreWhereRemovedText.TerrainKept(entry.Reason)}");
        var textures = quarter.TexturesKeptForObjectThatStays ? $"{RestoreWhereRemovedText.GroundTexturesPart} kept: {RestoreWhereRemovedText.TexturesKeptForObjectThatStays}" : string.Empty;
        return string.Join(AreasSeparator, new[] { RestoreWhereRemovedText.Pieces(quarter.Restored) }.Concat(kept).Append(textures).Where(piece => piece.Length > 0));
    }

    /// <param name="label">Names a record a guard found.</param>
    private static string RestoredNavmeshLine(CollectedObjects world, NavmeshOutcome outcome, Func<RecordKey, string> label)
    {
        var change = outcome.Judged.Change;
        var place = $"{change.Navmesh} in {Describe.Space(world, change.SpaceKey)}, cell {change.Cell}";
        if (RestoreOutcomes.KeptBecause(outcome, label) is { } kept)
        {
            return $"  Kept as changed: navmesh {place}: {kept}{ClearedAreaText.After(outcome.Judged.Area)}";
        }

        var undo = outcome.Judged.Undo ?? throw new UnreachableException($"The navmesh {change.Navmesh} goes back with its group but has no navmesh undo.");
        var group = outcome.Group ?? throw new UnreachableException($"The navmesh {change.Navmesh} goes back but is in no group.");
        var others = group.Members.Length - 1;
        var together = others > 0 ? $" together with {TextFormat.Count(others, "other navmesh", "other navmeshes")}" : string.Empty;
        return $"  Navmesh {place}: {RestoreWhereRemovedText.UndoVerb(undo)}{together}";
    }
}
