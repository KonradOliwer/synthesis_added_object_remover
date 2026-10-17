using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>
/// Finds the target objects on terrain that another mod changed, and proposes their removal. The terrain of each cell is compared
/// between its reference terrain (the terrain the target was built on) and the version the game shows; a difference counts when
/// a counting plugin's own edit made it, and that plugin is named as the changer. A terrain type change also needs the set share
/// of the area's vertices.
/// </summary>
public static class ChangedTerrainObjects
{
    /// <param name="loadOrder">Decides which of several changing plugins loads last.</param>
    /// <param name="records">Its terrain versions must have been read.</param>
    /// <remarks>When the step is off it reads no terrain and the result is <see cref="StepOutcome.SwitchedOff"/>.</remarks>
    public static ChangedTerrainResult Find(
        CollectedObjects world,
        IdentifiedMods mods,
        LoadOrderPlugins loadOrder,
        IPluginRecords records,
        IBaseObjectShapes shapes,
        Execution execution,
        ChangedTerrainSettings settings)
    {
        if (!settings.On) return ChangedTerrainResult.SwitchedOff;

        var check = new ObjectTerrainCheck(records, shapes, new TerrainPlugins(mods, loadOrder), settings);
        var checks = ParallelMap.Run(
            execution,
            world.Order,
            world.Targets.Length,
            index => check.Check(world.Targets[index]),
            ParallelMap.AutomaticRangeSize);
        ImmutableArray<ObjectOnChangedTerrain> hits = [.. checks.Select(objectCheck => objectCheck.Hit).OfType<ObjectOnChangedTerrain>()];
        return new ChangedTerrainResult(
            StepOutcome.Performed,
            hits,
            [.. hits.Select(ProposalOf)],
            FailuresOf(world.Targets, checks),
            check.CellsCompared(),
            checks.Count(objectCheck => objectCheck.WithoutTerrain));
    }

    private static ProposedRemoval ProposalOf(ObjectOnChangedTerrain hit) =>
        new(hit.Target, new RemovalReason.TerrainChanged(hit.ChangedBy, hit.Kind));

    private static ImmutableArray<PartFailure> FailuresOf(ImmutableArray<TargetObject> targets, ObjectCheck[] checks) =>
    [
        .. targets
            .Where(target => checks[target.Id.Index].Failure != null)
            .Select(target => new PartFailure(
                ChangedTerrainParts.TerrainCheck, checks[target.Id.Index].Failure, new FailureSubject(target.Key.ToString(), target.EditorId, target.Id.Index))),
    ];
}
