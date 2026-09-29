using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>What a moved marker must stay clear of.</summary>
/// <param name="Solids">Every visible object that is not a target object, replaced rivals included.</param>
/// <param name="Targets">The visible target objects; those <paramref name="Final"/> removes are no obstacles.</param>
internal sealed record Obstacles(ISolids Solids, IVisibleTargets Targets, Ledger Final);

/// <param name="Candidates">The kept markers to try to move, in target order.</param>
/// <param name="Handles">For the exterior cell each marker belongs to.</param>
/// <param name="Navmeshes">The winning navmeshes of each target space, not yet decoded.</param>
internal sealed record RelocationInput(
    ImmutableArray<TargetObject> Targets,
    ImmutableArray<LeftoverEvaluation> Candidates,
    Obstacles Obstacles,
    RecordHandles Handles,
    IReadOnlyDictionary<FormKey, List<CellNavmesh>> Navmeshes,
    TerrainHeights Terrain,
    ShapeCatalog Shapes,
    Execution Exec);
