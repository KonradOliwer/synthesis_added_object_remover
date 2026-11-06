using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

/// <summary>A target object on terrain that another mod changed.</summary>
/// <param name="ChangedBy">
/// The later loading of the plugins named for the counted changes around the object; null when that is the patch, whose change an
/// earlier patcher of the group made.
/// </param>
/// <param name="Kind">How the terrain and water the game shows differ from the accepted ones, at the vertices of the counted changes.</param>
/// <param name="Area">The evidence: per cell of the area that holds the counted changes, where they were counted, in the order of the area's cells.</param>
public readonly record struct ObjectOnChangedTerrain(TargetId Target, PluginName? ChangedBy, TerrainChangeKind Kind, ImmutableArray<CountedCell> Area);

/// <summary>Where the counted changes of a finding lie in one cell of its area.</summary>
/// <param name="LandCell">The cell of the worldspace whose land the object's space uses, which holds the heights and terrain types.</param>
/// <param name="WaterCell">The cell of the object's own worldspace, which holds the water.</param>
/// <param name="HeightVertices">The vertices whose height change counted, ascending.</param>
/// <param name="TypeVertices">The vertices whose terrain type change counted, ascending.</param>
/// <param name="WaterFlipped">
/// The vertices the counted water change covered or uncovered, ascending; empty when the water change does not count. Whether the
/// water covers a vertex depends on the ground there too, so a height that changes the ground the game shows can make or undo a flip.
/// </param>
public sealed record CountedCell(
    ExteriorCell LandCell, ExteriorCell WaterCell, ImmutableArray<int> HeightVertices, ImmutableArray<int> TypeVertices, ImmutableArray<int> WaterFlipped);
