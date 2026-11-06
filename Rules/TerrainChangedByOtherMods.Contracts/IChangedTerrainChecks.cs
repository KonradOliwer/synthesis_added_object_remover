using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

/// <summary>The changed-terrain checks over the target objects of one world.</summary>
public interface IChangedTerrainChecks
{
    /// <summary>Checks <paramref name="judged"/> with <paramref name="settings"/>; null when the settings could find nothing (<see cref="ChangedTerrainSettings.Runs"/>).</summary>
    /// <param name="judged">The targets to check, in target order.</param>
    ChangedTerrainFindings? Find(ImmutableArray<TargetId> judged, ChangedTerrainSettings settings, WorkOrder order, Execution execution);
}

/// <summary>The target objects found on changed terrain with one set of settings.</summary>
/// <param name="Hits">Each object on changed terrain, in target order.</param>
/// <param name="Failures">The target objects whose check failed unexpectedly, in target order; none of them is a hit.</param>
/// <param name="CellsCompared">
/// The exterior cells whose terrain versions were compared with their accepted terrain, each land cell once, even when a child
/// worldspace uses it too.
/// </param>
/// <param name="CellsComparedForWaterOnly">The exterior cells without checked land whose water was compared over the ground the game shows.</param>
/// <param name="ObjectsWithoutTerrain">
/// The objects checked that have no terrain to compare: in an interior, in a worldspace without terrain, or over cells with neither
/// checked land nor checked water.
/// </param>
public sealed record ChangedTerrainFindings(
    ImmutableArray<ObjectOnChangedTerrain> Hits,
    ImmutableArray<PartFailure> Failures,
    int CellsCompared,
    int CellsComparedForWaterOnly,
    int ObjectsWithoutTerrain);
