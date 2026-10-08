using System.Diagnostics;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>Finds the target objects too close to another mod's object, and proposes their removal.</summary>
internal static class TooCloseObjects
{
    public static TooCloseResult Find(TooCloseInput input, TooCloseOptions options)
    {
        var npcRule = NpcTooCloseRule.Create(
            options.Npcs,
            () => NpcStuckSearch.Create(
                input.Targets,
                input.VisibleTargets,
                input.Npcs ?? throw new InvalidOperationException("The NPC-stuck search needs the placed NPCs."),
                input.Shapes,
                input.Triangles));
        var (search, largeOtherObjects) = options.Zone switch
        {
            ZoneShape.BoundingBox => (FindInBoxZone(input, options, npcRule), 0),
            ZoneShape.ObjectShape => FindInShapeZone(input, options, npcRule),
            _ => throw new UnreachableException($"Unknown removal zone {options.Zone}."),
        };
        return new TooCloseResult(
            [.. search.Hits],
            [.. search.Hits.Select(hit => new ProposedRemoval(new TargetId(hit.TargetIndex), new RemovalReason.TooClose(hit.TooCloseTo.Id)))],
            search.Work,
            npcRule.StuckSearch?.GetSummary(search.Work.Npcs),
            largeOtherObjects,
            [.. search.Failures]);
    }

    private static TooCloseSearchResult FindInBoxZone(TooCloseInput input, TooCloseOptions options, NpcTooCloseRule npcRule) =>
        TooCloseSearch.FindTooCloseTargets(
            input.Targets, input.OtherModObjects, input.Shapes, options.Multiplier, npcRule, input.Order, input.Exec);

    private static (TooCloseSearchResult Result, int LargeOtherObjects) FindInShapeZone(TooCloseInput input, TooCloseOptions options, NpcTooCloseRule npcRule)
    {
        var search = ShapeZoneSearch.Create(
            input.Targets, input.VisibleTargets, input.OtherModObjects, input.Shapes, input.Triangles, options.Multiplier, npcRule);
        var result = search.FindTooCloseTargets(input.Order, input.Exec);
        return (result, search.LargeOtherObjects);
    }
}
