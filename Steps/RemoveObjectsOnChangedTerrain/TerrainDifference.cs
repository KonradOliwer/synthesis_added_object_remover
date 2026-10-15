using System.Diagnostics;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>How a terrain version differs from the reference terrain at one or more vertices.</summary>
internal readonly record struct TerrainChange(bool Height, bool TerrainType)
{
    public bool Any => Height || TerrainType;

    public TerrainChange With(TerrainChange other) => new(Height || other.Height, TerrainType || other.TerrainType);

    public TerrainChangeKind Kind => (Height, TerrainType) switch
    {
        (true, true) => TerrainChangeKind.HeightAndTerrainType,
        (true, false) => TerrainChangeKind.Height,
        (false, true) => TerrainChangeKind.TerrainType,
        _ => throw new UnreachableException("A terrain change without a kind."),
    };
}

/// <summary>A plugin's terrain version of a cell, decoded per vertex for the comparison.</summary>
/// <param name="Heights">Null where the record is deleted or has no height map.</param>
/// <param name="Types">The terrain type at each vertex; null exactly when terrain type changes are not counted.</param>
internal sealed record ComparedVersion(PluginName Plugin, float[]? Heights, TerrainType[]? Types);

/// <summary>How a terrain version differs from the reference terrain at a vertex, by the switched-on checks.</summary>
internal sealed class TerrainDifference(ChangedTerrainSettings settings)
{
    public TerrainChange At(ComparedVersion reference, ComparedVersion version, int vertex) => new(
        settings.CountHeightChanges && HeightDiffersAt(reference.Heights, version.Heights, vertex),
        TypeDiffersAt(reference.Types, version.Types, vertex));

    /// <remarks>Heights come in whole steps of 8 units, so the comparison is exact. A version without heights next to one with heights differs.</remarks>
    private bool HeightDiffersAt(float[]? reference, float[]? version, int vertex) => (reference, version) switch
    {
        (null, null) => false,
        ({ } referenceHeights, { } versionHeights) => MathF.Abs(versionHeights[vertex] - referenceHeights[vertex]) > settings.HeightTolerance,
        _ => true,
    };

    private bool TypeDiffersAt(TerrainType[]? reference, TerrainType[]? version, int vertex) =>
        reference is { } referenceTypes && version is { } versionTypes && referenceTypes[vertex] != versionTypes[vertex];
}
