using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// The counts the log states for the step that restores what the cleaned mod changed where it was removed: what went back, what
/// stays and why. Each method gives the facts of its line; a fact with a count of 0 is left out, so a line with none is not printed.
/// </summary>
internal static class RestoreCountLines
{
    /// <summary>What went back, per kind of change.</summary>
    public static IEnumerable<string> Restored(RestoreJudgements judged)
    {
        var objects = judged.Result.Objects.Where(change => change.IsRestored).ToList();
        if (objects.Count > 0)
        {
            yield return $"{TextFormat.Count(objects.Count, "object", "objects")} of other mods "
                + $"({TextLists.JoinCounts(objects.GroupBy(change => change.Kind).OrderBy(group => group.Key), group => RestoreWhereRemovedText.Kind(group.Key), group => group.Count(), Describe.ListSeparator)})";
        }

        var lands = judged.Result.Lands.Where(land => land.IsRestored).ToList();
        if (lands.Count > 0)
        {
            var quarters = lands.Sum(land => land.ChangedQuarters.Count(quarter => quarter.Restored.Any));
            var landCells = TextFormat.Count(lands.Count, "cell", "cells");
            yield return quarters > 0 ? $"terrain in {TextFormat.Count(quarters, "cell quarter", "cell quarters")} of {landCells}" : $"terrain of {landCells}";
        }

        var cells = judged.Result.Cells.Where(cell => cell.IsRestored).ToList();
        if (cells.Count > 0)
        {
            var parts = cells.SelectMany(cell => RestoreOutcomes.UndoneParts(cell)).GroupBy(part => part).OrderBy(group => group.Key);
            yield return $"settings of {TextFormat.Count(cells.Count, "cell", "cells")} "
                + $"({TextLists.JoinCounts(parts, group => RestoreWhereRemovedText.PartOfCell(group.Key), group => group.Count(), Describe.ListSeparator)})";
        }

        var navmeshes = judged.Navmeshes.Where(navmesh => navmesh.IsRestored).Select(navmesh => navmesh.Judged.Undo ?? throw MissingUndo(navmesh)).ToList();
        if (navmeshes.Count > 0)
        {
            var kinds = navmeshes.GroupBy(RestoreWhereRemovedText.Undo).OrderBy(group => group.Min());
            yield return $"{TextFormat.Count(navmeshes.Count, "navmesh", "navmeshes")} ({TextLists.JoinCounts(kinds, group => group.Key, group => group.Count(), Describe.ListSeparator)})";
        }
    }

    /// <summary>The count of the line that closes the log: the kinds of change that went back, without their breakdown.</summary>
    public static IEnumerable<string> RestoredInSummary(RestoreJudgements judged)
    {
        var objects = judged.Result.Objects.Count(change => change.IsRestored);
        if (objects > 0) yield return TextFormat.Count(objects, "object", "objects");

        var lands = judged.Result.Lands.Count(land => land.IsRestored);
        if (lands > 0) yield return $"terrain in {TextFormat.Count(lands, "cell", "cells")}";

        var cells = judged.Result.Cells.Count(cell => cell.IsRestored);
        if (cells > 0) yield return $"settings of {TextFormat.Count(cells, "cell", "cells")}";

        var navmeshes = judged.Navmeshes.Count(navmesh => navmesh.IsRestored);
        if (navmeshes > 0) yield return TextFormat.Count(navmeshes, "navmesh", "navmeshes");
    }

    /// <summary>What stays because a guard found something that stays: objects, terrain points, water levels and navmeshes.</summary>
    public static IEnumerable<string> LeftAsChanged(RestoreJudgements judged)
    {
        var refusals = judged.Result.Objects.Select(change => change.Refused).OfType<PlacedRefusal>().ToList();
        if (refusals.Count > 0)
        {
            var kinds = refusals.GroupBy(RestoreWhereRemovedText.RefusalKind).OrderBy(group => group.First().Failure is not null).ThenBy(group => group.First().Guard);
            yield return $"{TextFormat.Count(refusals.Count, "object", "objects")} ({TextLists.JoinCounts(kinds, group => group.Key, group => group.Count(), Describe.ListSeparator)})";
        }

        var kept = judged.Result.Lands.SelectMany(land => land.Kept).ToList();
        var points = kept.Sum(entry => entry.Points.Count);
        if (points > 0)
        {
            var reasons = kept.GroupBy(entry => entry.Reason).OrderBy(group => group.Key);
            yield return $"terrain at {TextFormat.Count(points, "point", "points")} "
                + $"({TextLists.JoinCounts(reasons, group => RestoreWhereRemovedText.TerrainKept(group.Key), group => group.Sum(entry => entry.Points.Count), Describe.ListSeparator)})";
        }

        var water = judged.Result.Cells.Count(cell => cell.WaterKeptBy is not null);
        if (water > 0) yield return $"water in {TextFormat.Count(water, "cell", "cells")}";

        var refused = RefusedGroups(judged.Result).ToList();
        if (refused.Count > 0)
        {
            var guards = refused.GroupBy(group => group.Guard).OrderBy(group => group.Key);
            yield return $"{TextFormat.Count(refused.Sum(group => group.Members), "navmesh", "navmeshes")} "
                + $"({TextLists.JoinCounts(guards, group => RestoreWhereRemovedText.Guard(group.Key), group => group.Sum(member => member.Members), Describe.ListSeparator)})";
        }
    }

    private static IEnumerable<(NavmeshGuard Guard, int Members)> RefusedGroups(RestoreWhereRemovedResult result)
    {
        foreach (var group in result.NavmeshGroups)
        {
            if (group.Refused is { } refusal) yield return (refusal.Guard, group.Members.Length);
        }
    }

    /// <summary>
    /// What a later plugin, or the patch, changed again after the cleaned mod in the changes judged, so it stays: parts of objects and
    /// cells, terrain, navmeshes.
    /// </summary>
    public static IEnumerable<string> LeftToLaterPlugins(RestoreJudgements judged)
    {
        var objectParts = judged.Result.Objects.SelectMany(change => change.Change.Earlier.LeftToLaterPlugins.Select(part => (Record: change.Change.Key, Plugin: part.Value))).ToList();
        if (objectParts.Count > 0)
        {
            var objects = objectParts.Select(part => part.Record).Distinct().Count();
            yield return $"{TextFormat.Count(objectParts.Count, "part", "parts")} of {TextFormat.Count(objects, "object", "objects")} ({ByPlugin(objectParts.Select(part => part.Plugin))})";
        }

        var terrain = TerrainLeftToLater(judged).ToList();
        if (terrain.Count > 0) yield return $"terrain ({string.Join(Describe.ListSeparator, terrain)})";

        var cellParts = judged.Result.Cells
            .SelectMany(cell => cell.Change.Parts.Select(part => part.Outcome).OfType<PartOutcome.LeftToLaterPlugin>().Select(later => (Record: cell.Change.Cell, later.Plugin)))
            .ToList();
        if (cellParts.Count > 0)
        {
            var cells = cellParts.Select(part => part.Record).Distinct().Count();
            yield return $"{TextFormat.Count(cellParts.Count, "part", "parts")} of {TextFormat.Count(cells, "cell", "cells")} ({ByPlugin(cellParts.Select(part => part.Plugin))})";
        }

        var navmeshes = judged.Navmeshes.Count(navmesh => RestoreOutcomes.LeftToLaterPlugins(navmesh.Judged.Change).Any());
        if (navmeshes > 0) yield return TextFormat.Count(navmeshes, "navmesh", "navmeshes");
    }

    /// <summary>
    /// What stays only because of the area around it, of the given verdict: objects, cells, terrain quarters, the rest of land records
    /// that changed nothing else, and navmeshes.
    /// </summary>
    public static IEnumerable<string> KeptForArea(RestoreJudgements judged, AreaVerdict verdict)
    {
        var objects = judged.Result.Objects.Count(change => change.KeptForArea == verdict);
        if (objects > 0) yield return TextFormat.Count(objects, "object", "objects");

        var cells = judged.Result.Cells.Count(cell => cell.KeptForArea == verdict);
        if (cells > 0) yield return TextFormat.Count(cells, "cell", "cells");

        var quarters = judged.Result.Lands.Sum(land => land.ChangedQuarters.Count(quarter => quarter.KeptForArea == verdict));
        if (quarters > 0) yield return $"terrain in {TextFormat.Count(quarters, "cell quarter", "cell quarters")}";

        var rests = judged.Result.Lands.Count(land => RestoreOutcomes.HoldsOnlyTheRest(land) && land.RestKeptForArea == verdict);
        if (rests > 0) yield return $"the rest of {TextFormat.Count(rests, "land record", "land records")}";

        var navmeshes = judged.Navmeshes.Count(navmesh => navmesh.Judged.KeptForArea == verdict);
        if (navmeshes > 0) yield return TextFormat.Count(navmeshes, "navmesh", "navmeshes");
    }

    /// <summary>What never goes back whatever its area: the count of changes per reason.</summary>
    public static IEnumerable<string> NeverRestored(RestoreJudgements judged)
    {
        var reasons = judged.Result.Objects.Select(change => change.Never)
            .Concat(judged.Result.Cells.Select(cell => cell.Never))
            .Concat(judged.Result.Lands.Select(land => land.Never))
            .Concat(judged.Navmeshes.Select(navmesh => navmesh.Judged.Never))
            .OfType<NeverRestoredReason>()
            .ToList();
        if (reasons.Count > 0)
        {
            yield return $"{TextFormat.Count(reasons.Count, "change", "changes")} "
                + $"({TextLists.JoinCounts(reasons.GroupBy(reason => reason).OrderBy(group => group.Key), group => RestoreWhereRemovedText.Never(group.Key), group => group.Count(), Describe.ListSeparator)})";
        }
    }

    private static IEnumerable<string> TerrainLeftToLater(RestoreJudgements judged)
    {
        var later = judged.Result.Lands.Select(land => land.Change.LeftToLater).ToList();
        var heights = later.Sum(entry => entry.Points.Count);
        if (heights > 0) yield return RestoreWhereRemovedText.Heights(heights);

        var colours = later.Sum(entry => entry.Colours.Count);
        if (colours > 0) yield return RestoreWhereRemovedText.VertexColours(colours);

        var textures = later.Sum(entry => entry.Quarters.Quarters.Count());
        if (textures > 0) yield return TextFormat.Count(textures, "texture quarter", "texture quarters");

        var rests = later.Count(entry => entry.Rest);
        if (rests > 0) yield return $"the rest of {TextFormat.Count(rests, "land record", "land records")}";
    }

    /// <summary>E.g. "Later.esp 4, Patch.esp 1": each plugin with the number of parts it last changed, the most first.</summary>
    private static string ByPlugin(IEnumerable<PluginName> plugins) =>
        string.Join(
            Describe.ListSeparator,
            KeyedGroups.Rank(KeyedGroups.CountBy(plugins.Select(plugin => plugin.ToString()), plugin => plugin, StringComparer.Ordinal), StringComparer.Ordinal)
                .Select(entry => $"{entry.Key} {TextFormat.Count(entry.Value)}"));

    private static InvalidOperationException MissingUndo(NavmeshOutcome navmesh) =>
        new($"The navmesh {navmesh.Judged.Change.Navmesh} goes back with its group but has no navmesh undo.");
}
