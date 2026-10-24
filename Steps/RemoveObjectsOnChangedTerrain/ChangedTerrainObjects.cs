using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>
/// Finds the target objects on terrain that another mod changed, and proposes their removal. At each vertex the version the game
/// shows is compared with the accepted height and terrain type, each from the highest version trusted for it; a difference counts,
/// and the plugin whose version first holds the shown value is named as the changer. A terrain type change also needs the set share
/// of the area's vertices. The cell's water is compared with the accepted water, made of the cell's water flag and own height and
/// its worldspace's default height, each from the highest version trusted for it, and counts when its change covered or uncovered
/// the set share of the area's vertices.
/// </summary>
public static class ChangedTerrainObjects
{
    /// <param name="loadOrder">Decides which of several changing plugins loads last.</param>
    /// <param name="records">Its terrain and water versions must have been read.</param>
    /// <remarks>When the step is off it reads no terrain and the result is <see cref="StepOutcome.SwitchedOff"/>.</remarks>
    public static ChangedTerrainResult Find(
        CollectedObjects world,
        ModTrustTable trust,
        LoadOrderPlugins loadOrder,
        IPluginRecords records,
        IBaseObjectShapes shapes,
        Execution execution,
        ChangedTerrainSettings settings)
    {
        if (!settings.On) return ChangedTerrainResult.SwitchedOff;

        var check = CreateCheck(trust, loadOrder, records, shapes, settings);
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

    /// <summary>The step's check of one target object at a given placement; it finds nothing when the step is off.</summary>
    /// <param name="loadOrder">Decides which of several changing plugins loads last.</param>
    /// <param name="records">Its terrain and water versions must have been read.</param>
    public static IPlacementRule CreatePlacementRule(
        CollectedObjects world,
        ModTrustTable trust,
        LoadOrderPlugins loadOrder,
        IPluginRecords records,
        IBaseObjectShapes shapes,
        ChangedTerrainSettings settings) =>
        new ChangedTerrainPlacementRule(world.Targets, settings.On ? CreateCheck(trust, loadOrder, records, shapes, settings) : null);

    /// <summary>How the step words a finding as the reason of a removal.</summary>
    internal static RemovalReason ReasonOf(ObjectOnChangedTerrain hit) => new RemovalReason.TerrainChanged(hit.ChangedBy, hit.Kind);

    private static ObjectTerrainCheck CreateCheck(
        ModTrustTable trust, LoadOrderPlugins loadOrder, IPluginRecords records, IBaseObjectShapes shapes, ChangedTerrainSettings settings) =>
        new(records, shapes, new TerrainPlugins(trust, loadOrder), settings);

    private static ProposedRemoval ProposalOf(ObjectOnChangedTerrain hit) => new(hit.Target, ReasonOf(hit));

    private static ImmutableArray<PartFailure> FailuresOf(ImmutableArray<TargetObject> targets, ObjectCheck[] checks) =>
    [
        .. targets
            .Where(target => checks[target.Id.Index].Failure != null)
            .Select(target => new PartFailure(
                ChangedTerrainParts.TerrainCheck, checks[target.Id.Index].Failure, new FailureSubject(target.Key.ToString(), target.EditorId, target.Id.Index))),
    ];
}
