using System.Diagnostics;

namespace AddedObjectRemover;

/// <summary>Finds the target objects too close to another mod's object, and proposes their removal.</summary>
internal static class Clashes
{
    public static ClashResult Find(ClashInput input, ClashOptions options)
    {
        var npcRule = NpcClashRule.Create(
            options.Npcs,
            () => NpcStuckSearch.Create(
                input.Targets,
                input.Looks,
                input.Npcs ?? throw new InvalidOperationException("The NPC-stuck search needs the placed NPCs."),
                input.Shapes,
                input.Triangles));
        var (search, largeRivals) = options.Zone switch
        {
            ZoneShape.BoundingBox => (FindInBoxZone(input, options, npcRule), 0),
            ZoneShape.ObjectShape => FindInShapeZone(input, options, npcRule),
            _ => throw new UnreachableException($"Unknown removal zone {options.Zone}."),
        };
        return new ClashResult(
            [.. search.Hits],
            [.. search.Hits.Select(hit => new Proposal(new TargetId(hit.TargetIndex), new Cause.TooClose(hit.TooCloseTo.Id)))],
            search.Work,
            npcRule.StuckSearch?.GetSummary(search.Work.Npcs),
            largeRivals);
    }

    private static ClashSearchResult FindInBoxZone(ClashInput input, ClashOptions options, NpcClashRule npcRule) =>
        TooCloseSearch.FindTooCloseTargets(
            input.Targets, input.Looks, input.Rivals, input.Shapes, options.Multiplier, npcRule, input.Order, input.Exec);

    private static (ClashSearchResult Result, int LargeRivals) FindInShapeZone(ClashInput input, ClashOptions options, NpcClashRule npcRule)
    {
        var search = input.Timer.Time(
            TimedPhase.ShapeZoneIndexBuild,
            () => ShapeZoneSearch.Create(
                input.Targets, input.Looks, input.Rivals, input.Shapes, input.Triangles, options.Multiplier, npcRule));
        var result = input.Timer.Time(
            TimedPhase.ShapeZoneSearch,
            () => search.FindTooCloseTargets(input.Looks, input.Order, input.Exec));
        return (result, search.LargeOtherObjects);
    }
}
