using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
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
    /// <param name="LoadOrder">Decides which of several changing plugins loads last.</param>
    /// <param name="Records">Its terrain and water versions must have been read.</param>
    /// <param name="Decisions">The run's decisions so far, which the step's round is added to.</param>
    public sealed record Input(
        CollectedObjects World,
        IRunDecisions Decisions,
        AcceptedWorld Accepted,
        LoadOrderPlugins LoadOrder,
        IPluginRecords Records,
        IBaseObjectShapes Shapes,
        Execution Execution,
        IEnumerable<ObjectsJudgedWith<ChangedTerrainSettings>> Groups);

    /// <summary>
    /// Runs once for each distinct set of settings over the objects judged with it, and proposes the removals of all of them in target order.
    /// </summary>
    /// <remarks>
    /// A pass whose step is off reads no terrain and is <see cref="StepOutcome.SwitchedOff"/>. A group switched on that counts no change
    /// or has no area runs as off, so it merges with the groups switched off.
    /// </remarks>
    public static Decided<ChangedTerrainResult> Find(Input input)
    {
        ChangedTerrainResult result = new(
            [
                .. ObjectsJudgedWith<ChangedTerrainSettings>.MergeEqualSettings(input.Groups.Select(group => group with { Settings = group.Settings.AsRun }))
                    .Select(group => FindPass(input.World, input.Accepted, input.LoadOrder, input.Records, input.Shapes, input.Execution, group)),
            ]);
        return new Decided<ChangedTerrainResult>(result, input.Decisions.Apply(RoundKind.ChangedTerrain, result.Proposals));
    }

    /// <param name="LoadOrder">Decides which of several changing plugins loads last.</param>
    /// <param name="Records">Its terrain and water versions must have been read.</param>
    public sealed record PlacementRuleInput(AcceptedWorld Accepted, LoadOrderPlugins LoadOrder, IPluginRecords Records, IBaseObjectShapes Shapes, ChangedTerrainSettings Settings);

    /// <summary>The step's check of one target object at a given placement; it finds nothing when the step is off.</summary>
    public static IPlacementRule CreatePlacementRule(PlacementRuleInput input) =>
        new ChangedTerrainPlacementRule(input.Settings.Runs ? CreateCheck(input.Accepted, input.LoadOrder, input.Records, input.Shapes, input.Settings) : null);

    /// <summary>How the step words a finding as the reason of a removal.</summary>
    internal static RemovalReason ReasonOf(ObjectOnChangedTerrain hit) => new TerrainChanged(hit.ChangedBy, hit.Kind);

    private static ChangedTerrainPass FindPass(
        CollectedObjects world,
        AcceptedWorld accepted,
        LoadOrderPlugins loadOrder,
        IPluginRecords records,
        IBaseObjectShapes shapes,
        Execution execution,
        ObjectsJudgedWith<ChangedTerrainSettings> group)
    {
        if (!group.Settings.Runs) return ChangedTerrainPass.SwitchedOff(group.Objects);

        var check = CreateCheck(accepted, loadOrder, records, shapes, group.Settings);
        List<int> indexes = [.. group.Objects.Select(target => target.Index)];
        var checks = ParallelMap.RunOver(
            execution,
            world.Order,
            indexes,
            index => check.Check(world.Targets[index]),
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
