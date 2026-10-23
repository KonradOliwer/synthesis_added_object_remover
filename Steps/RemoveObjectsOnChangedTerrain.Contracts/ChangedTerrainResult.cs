using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

/// <summary>The target objects on terrain that another mod changed.</summary>
/// <param name="Outcome"><see cref="StepOutcome.Performed"/>, or <see cref="StepOutcome.SwitchedOff"/> when the step is off; the result then holds nothing.</param>
/// <param name="Hits">Each object on changed terrain, in target order.</param>
/// <param name="Proposals">A removal proposal for each hit, in target order.</param>
/// <param name="Failures">The target objects whose check failed unexpectedly, in target order; none of them is a hit.</param>
/// <param name="CellsCompared">
/// The exterior cells whose terrain versions were compared with their accepted terrain, each land cell once, even when a child
/// worldspace uses it too.
/// </param>
/// <param name="ObjectsWithoutTerrain">The objects the step would check that have no terrain to compare: in an interior, in a worldspace without terrain, or over cells no trusted plugin has a land record of.</param>
public sealed record ChangedTerrainResult(
    StepOutcome Outcome,
    ImmutableArray<ObjectOnChangedTerrain> Hits,
    ImmutableArray<ProposedRemoval> Proposals,
    ImmutableArray<PartFailure> Failures,
    int CellsCompared,
    int ObjectsWithoutTerrain)
{
    public static ChangedTerrainResult SwitchedOff { get; } = new(StepOutcome.SwitchedOff, [], [], [], CellsCompared: 0, ObjectsWithoutTerrain: 0);
}

/// <summary>A target object on terrain that another mod changed.</summary>
/// <param name="ChangedBy">
/// The later loading of the plugins named for the counted changes around the object; null when that is the patch, whose change an
/// earlier patcher of the group made.
/// </param>
/// <param name="Kind">How the terrain and water the game shows differ from the accepted ones, at the vertices of the counted changes.</param>
public readonly record struct ObjectOnChangedTerrain(TargetId Target, PluginName? ChangedBy, TerrainChangeKind Kind);
