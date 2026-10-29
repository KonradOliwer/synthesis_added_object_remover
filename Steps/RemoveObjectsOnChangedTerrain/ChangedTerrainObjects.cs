using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
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
    /// <summary>
    /// Runs once for each distinct set of settings over the objects judged with it, and proposes the removals of all of them in target order.
    /// </summary>
    /// <param name="loadOrder">Decides which of several changing plugins loads last.</param>
    /// <param name="records">Its terrain and water versions must have been read.</param>
    /// <remarks>A pass whose step is off reads no terrain and is <see cref="StepOutcome.SwitchedOff"/>.</remarks>
    public static ChangedTerrainResult Find(
        CollectedObjects world,
        AcceptedWorld accepted,
        LoadOrderPlugins loadOrder,
        IPluginRecords records,
        IBaseObjectShapes shapes,
        Execution execution,
        IEnumerable<ObjectsJudgedWith<ChangedTerrainSettings>> groups) =>
        new([.. JudgedGroups.MergeEqualSettings(groups).Select(group => FindPass(world, accepted, loadOrder, records, shapes, execution, group))]);

    /// <summary>The step's check of one target object at a given placement; it finds nothing when the step is off.</summary>
    /// <param name="loadOrder">Decides which of several changing plugins loads last.</param>
    /// <param name="records">Its terrain and water versions must have been read.</param>
    public static IPlacementRule CreatePlacementRule(
        AcceptedWorld accepted,
        LoadOrderPlugins loadOrder,
        IPluginRecords records,
        IBaseObjectShapes shapes,
        ChangedTerrainSettings settings) =>
        new ChangedTerrainPlacementRule(settings.On ? CreateCheck(accepted, loadOrder, records, shapes, settings) : null);

    /// <summary>How the step words a finding as the reason of a removal.</summary>
    internal static RemovalReason ReasonOf(ObjectOnChangedTerrain hit) => new RemovalReason.TerrainChanged(hit.ChangedBy, hit.Kind);

    private static ChangedTerrainPass FindPass(
        CollectedObjects world,
        AcceptedWorld accepted,
        LoadOrderPlugins loadOrder,
        IPluginRecords records,
        IBaseObjectShapes shapes,
        Execution execution,
        ObjectsJudgedWith<ChangedTerrainSettings> group)
    {
        if (!group.Settings.On) return ChangedTerrainPass.SwitchedOff(group.Objects);

        var check = CreateCheck(accepted, loadOrder, records, shapes, group.Settings);
        List<int> indexes = [.. group.Objects.Select(target => target.Index)];
        var checks = ParallelMap.RunOver(
            execution,
            world.Order,
            indexes,
            static () => (object?)null,
            (index, _) => check.Check(world.Targets[index]),
            ParallelMap.AutomaticRangeSize);
        ImmutableArray<ObjectOnChangedTerrain> hits = [.. checks.Select(objectCheck => objectCheck.Hit).OfType<ObjectOnChangedTerrain>()];
        return new ChangedTerrainPass(
            group.Objects,
            StepOutcome.Performed,
            hits,
            [.. hits.Select(ProposalOf)],
            FailuresOf(world.Targets, indexes, checks),
            check.CellsCompared(),
            checks.Count(objectCheck => objectCheck.WithoutTerrain));
    }

    private static ObjectTerrainCheck CreateCheck(
        AcceptedWorld accepted, LoadOrderPlugins loadOrder, IPluginRecords records, IBaseObjectShapes shapes, ChangedTerrainSettings settings) =>
        new(records, shapes, new TerrainPlugins(accepted, loadOrder), settings);

    private static ProposedRemoval ProposalOf(ObjectOnChangedTerrain hit) => new(hit.Target, ReasonOf(hit));

    /// <param name="indexes">The target index of each check.</param>
    private static ImmutableArray<PartFailure> FailuresOf(ImmutableArray<TargetObject> targets, IReadOnlyList<int> indexes, ObjectCheck[] checks) =>
    [
        .. Enumerable.Range(0, checks.Length)
            .Where(position => checks[position].Failure != null)
            .Select(position => FailureOf(targets[indexes[position]], checks[position].Failure)),
    ];

    private static PartFailure FailureOf(TargetObject target, string? failure) =>
        new(ChangedTerrainParts.TerrainCheck, failure, new FailureSubject(target.Key.ToString(), target.EditorId, target.Id.Index));
}
