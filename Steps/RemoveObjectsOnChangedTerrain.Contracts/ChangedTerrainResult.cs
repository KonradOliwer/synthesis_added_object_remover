using System.Collections.Immutable;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

/// <summary>The target objects on terrain that another mod changed, found with one set of settings.</summary>
/// <param name="Objects">The target objects the pass judged, in target order.</param>
/// <param name="Outcome"><see cref="StepOutcome.Performed"/>, or <see cref="StepOutcome.SwitchedOff"/> when the pass is off; the pass then holds nothing.</param>
/// <param name="Hits">Each object on changed terrain, in target order.</param>
/// <param name="Proposals">A removal proposal for each hit, in target order.</param>
/// <param name="Failures">The target objects whose check failed unexpectedly, in target order; none of them is a hit.</param>
/// <param name="CellsCompared">
/// The exterior cells whose terrain versions were compared with their accepted terrain, each land cell once, even when a child
/// worldspace uses it too.
/// </param>
/// <param name="CellsComparedForWaterOnly">The exterior cells without checked land whose water was compared over the ground the game shows.</param>
/// <param name="ObjectsWithoutTerrain">
/// The objects the pass would check that have no terrain to compare: in an interior, in a worldspace without terrain, or over cells
/// with neither checked land nor checked water.
/// </param>
public sealed record ChangedTerrainPass(
    ImmutableArray<TargetId> Objects,
    StepOutcome Outcome,
    ImmutableArray<ObjectOnChangedTerrain> Hits,
    ImmutableArray<ProposedRemoval> Proposals,
    ImmutableArray<PartFailure> Failures,
    int CellsCompared,
    int CellsComparedForWaterOnly,
    int ObjectsWithoutTerrain) : IJudgedPass
{
    public static ChangedTerrainPass SwitchedOff(ImmutableArray<TargetId> objects) =>
        new(objects, StepOutcome.SwitchedOff, [], [], [], CellsCompared: 0, CellsComparedForWaterOnly: 0, ObjectsWithoutTerrain: 0);
}

/// <summary>The target objects on terrain that another mod changed.</summary>
public sealed record ChangedTerrainResult(ImmutableArray<ChangedTerrainPass> Passes) : JudgedPasses<ChangedTerrainPass>(Passes);
