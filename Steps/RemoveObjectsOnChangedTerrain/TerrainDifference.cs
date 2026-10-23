using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>A plugin's terrain version of a cell, decoded per vertex for the comparison.</summary>
/// <param name="Heights">Null where the record is deleted or has no height map.</param>
/// <param name="Types">The terrain type at each vertex; null exactly when terrain type changes are not counted.</param>
internal sealed record ComparedVersion(PluginName Plugin, float[]? Heights, TerrainType[]? Types);

/// <summary>Whether two terrain versions differ at a vertex, in height beyond the height tolerance or in terrain type.</summary>
internal sealed class TerrainDifference(ChangedTerrainSettings settings)
{
    /// <remarks>
    /// A version without heights next to one with heights differs. The tolerance applies whether height changes are counted or not.
    /// </remarks>
    public bool HeightDiffersAt(ComparedVersion one, ComparedVersion other, int vertex) => (one.Heights, other.Heights) switch
    {
        (null, null) => false,
        ({ } oneHeights, { } otherHeights) => MathF.Abs(otherHeights[vertex] - oneHeights[vertex]) > settings.HeightTolerance,
        _ => true,
    };

    /// <remarks>Never true when terrain type changes are not counted: the versions then hold no types.</remarks>
    public bool TypeDiffersAt(ComparedVersion one, ComparedVersion other, int vertex) =>
        one.Types is { } oneTypes && other.Types is { } otherTypes && oneTypes[vertex] != otherTypes[vertex];
}
