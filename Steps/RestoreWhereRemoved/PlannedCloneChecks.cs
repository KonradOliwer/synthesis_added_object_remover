using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Judges each planned copy where its original stands now by the changed-terrain rule a later step for the same cleaned mod judges
/// the cleaned mod's own objects with, so that step does not act on it: a copy the rule finds, or whose check stops, is refused. It
/// runs once, before the restore's passes, on the ground as the game shows it now. That is the ground the patch writes where the
/// rule looks: around each copy that goes back, the passes keep the heights in every area the changed-terrain step checks, and the
/// quarter textures and cell water levels it reads where it counts those changes (<see cref="TerrainStepAreas"/>). The rules that
/// depend on what goes back are judged in the passes (<see cref="CloneGuards"/>).
/// </summary>
internal static class PlannedCloneChecks
{
    /// <param name="judged">Every placed record judged by the never-rules, its areas and the copy rule, in key order.</param>
    /// <param name="changedTerrain">The changed-terrain check with the settings of the cleaned mod's own objects.</param>
    /// <returns><paramref name="judged"/> with each refused copy's refusal, in the same order.</returns>
    public static PlacedChangeJudged[] Judge(PlacedChangeJudged[] judged, IReadOnlyList<TargetObject> targets, IPlacementRule changedTerrain, Execution execution)
    {
        int[] planned = [.. Enumerable.Range(0, judged.Length).Where(index => judged[index].Cloning is { RefusedBy: null })];
        if (planned.Length == 0) return judged;

        TargetObject[] clones = [.. planned.Select((index, position) => CloneCandidates.AsClone(targets[judged[index].Change.Target!.Value.Index] with { Id = new TargetId(position) }))];
        var terrainChecks = ParallelMap.Run(execution, clones.Length, position => changedTerrain.CheckWhereItStands(clones[position]), ParallelMap.AutomaticRangeSize);
        var result = judged.ToArray();
        for (var position = 0; position < planned.Length; position++)
        {
            var (index, check) = (planned[position], terrainChecks[position]);
            if (check.Clash is not null || check.Failure is not null)
            {
                result[index] = ChangeJudge.WithCloning(judged[index], judged[index].Cloning! with { RefusedBy = CloneRefusal.OnChangedTerrainAsCleanedModsObject, Failure = check.Failure });
            }
        }

        return result;
    }
}
