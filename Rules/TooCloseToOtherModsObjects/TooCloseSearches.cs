using System.Collections.Immutable;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects;

/// <summary>The searches over the target objects of <paramref name="world"/>, sharing <paramref name="npcs"/>, whose bodies are measured once.</summary>
internal sealed class TooCloseSearches(TooCloseWorld world, INpcsThatCanSpawn npcs) : ITooCloseSearches
{
    public TooCloseFindings? Find(ImmutableArray<TargetId> judged, TooCloseSettings settings, WorkOrder order, Execution execution)
    {
        var zones = ZoneGrowth.Active(settings);
        if (zones.IsEmpty) return null;

        var (search, npcRule) = TooCloseRules.CreateSearch(world, npcs, settings, zones);
        var found = search.FindTooCloseTargets(judged, order, execution);
        return new TooCloseFindings(
            [.. found.Hits],
            found.Work,
            npcRule.StuckSearch?.GetSummary(found.Work.Npcs),
            search.LargeOtherObjects,
            found.Failures,
            [.. zones.Select(zone => zone.Cause)]);
    }
}
