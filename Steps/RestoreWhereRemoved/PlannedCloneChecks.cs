using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Judges each planned copy where its original stands now by the rules a later step for the same cleaned mod judges the cleaned
/// mod's own objects with, so that step does not act on it: a copy the too-close or the changed-terrain rule finds, or whose check
/// stops, is refused. It runs once, before the restore's passes, with every placed record whose areas let it go back going back:
/// the passes only refuse, which takes causes away, so a copy that passes here passes in every pass.
/// </summary>
internal static class PlannedCloneChecks
{
    /// <param name="judged">Every placed record judged by the never-rules, its areas and the copy rule, in key order.</param>
    /// <param name="createTooClose">Built only when a copy is planned, since it reads the trust of every placed record going back.</param>
    /// <param name="changedTerrain">The changed-terrain check with the settings of the cleaned mod's own objects.</param>
    /// <returns><paramref name="judged"/> with each refused copy's refusal, in the same order.</returns>
    public static PlacedChangeJudged[] Judge(
        PlacedChangeJudged[] judged, IReadOnlyList<TargetObject> targets, Func<CloneTooCloseCheck> createTooClose, IPlacementRule changedTerrain, Execution execution)
    {
        int[] planned = [.. Enumerable.Range(0, judged.Length).Where(index => judged[index].Cloning is { RefusedBy: null })];
        if (planned.Length == 0) return judged;

        TargetObject[] clones = [.. planned.Select((index, position) => CloneWhereItStandsNow(judged[index].Change, new TargetId(position), targets))];
        var tooCloseChecks = createTooClose().Check(clones);
        var terrainChecks = ParallelMap.Run(execution, clones.Length, position => changedTerrain.CheckWhereItStands(clones[position]), ParallelMap.AutomaticRangeSize);
        var result = judged.ToArray();
        for (var position = 0; position < planned.Length; position++)
        {
            var index = planned[position];
            if (RefusalOf(tooCloseChecks[position], terrainChecks[position]) is { } refused)
            {
                result[index] = ChangeJudge.WithCloning(judged[index], judged[index].Cloning! with { RefusedBy = refused.By, Failure = refused.Failure });
            }
        }

        return result;
    }

    /// <summary>The copy as a later step reads it: one of the cleaned mod's objects, where the original stands now.</summary>
    private static TargetObject CloneWhereItStandsNow(RecordChangedByCleanedMod change, TargetId id, IReadOnlyList<TargetObject> targets) =>
        targets[change.Target!.Value.Index] with { Id = id, Origin = TargetOrigin.CleanedMod, CloneOf = change.Key };

    /// <returns>The first rule that finds the copy or whose check stopped, too close first; null when neither does.</returns>
    private static (CloneRefusal By, string? Failure)? RefusalOf(PlacementCheck tooClose, PlacementCheck changedTerrain)
    {
        if (tooClose.Clash is not null || tooClose.Failure is not null) return (CloneRefusal.TooCloseAsCleanedModsObject, tooClose.Failure);
        if (changedTerrain.Clash is not null || changedTerrain.Failure is not null) return (CloneRefusal.OnChangedTerrainAsCleanedModsObject, changedTerrain.Failure);
        return null;
    }
}
