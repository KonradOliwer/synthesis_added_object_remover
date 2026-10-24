using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>
/// Builds a cell's accepted terrain, each vertex's height and terrain type from its own accepted version, and compares the version
/// the game shows with it vertex by vertex. Thread-safe.
/// </summary>
/// <remarks>
/// Height and terrain type are decided apart, so a plugin trusted for its own changes that only repaints a vertex does not make an
/// untrusted mod's height there accepted. Such a plugin owns a height only beyond the height tolerance, so a land record it re-saved
/// with rounding noise owns nothing.
/// </remarks>
internal sealed class CellTerrainComparison(TerrainPlugins plugins, TerrainTypes types, ChangedTerrainSettings settings)
{
    /// <summary>The positions of the vertex properties in the results of <see cref="AcceptedVersions.Decide"/>.</summary>
    private const int HeightProperty = 0;
    private const int TypeProperty = 1;

    private readonly TerrainDifference _difference = new(settings);

    /// <param name="versions">The cell's terrain versions in load order.</param>
    /// <returns>Null when no vertex has an accepted height or terrain type: no trusted plugin has a version of the cell.</returns>
    public CellTerrain? Compare(TerrainVersion[] versions)
    {
        if (versions.Length == 0) return null;
        var compared = versions.Select(ToCompared).ToArray();
        var trusts = versions.Select(version => plugins.TrustOf(version.Plugin)).ToArray();
        var vertices = Enumerable.Range(0, CellTerrain.VertexCount).Select(vertex => DecideAt(compared, trusts, vertex)).ToArray();
        if (!vertices.Any(vertex => vertex.IsChecked)) return null;

        var shown = compared[^1].Heights;
        return new CellTerrain(ComposeAcceptedHeights(compared, vertices, shown), shown, ChangesOf(compared, vertices), FindUnchecked(vertices));
    }

    private ComparedVersion ToCompared(TerrainVersion version) =>
        new(version.Plugin, version.Heights, settings.CountTerrainTypeChanges ? types.AtEveryVertex(version) : null);

    /// <remarks>The height is decided also when height changes are not counted: the accepted heights still say where the ground was.</remarks>
    private VertexAcceptance DecideAt(ComparedVersion[] compared, VersionTrust[] trusts, int vertex)
    {
        var properties = AcceptedVersions.Decide(
            trusts,
            [
                (one, other) => _difference.HeightDiffersAt(compared[one], compared[other], vertex),
                (one, other) => _difference.TypeDiffersAt(compared[one], compared[other], vertex),
            ]);
        return new VertexAcceptance(properties[HeightProperty], properties[TypeProperty]);
    }

    /// <summary>
    /// Each vertex's height from its accepted version. Where that version holds no heights, or the vertex has no accepted height, the
    /// shown height stands in: the ground there is taken as the game shows it.
    /// </summary>
    /// <returns>Null when a vertex needs the shown height and the shown version holds none.</returns>
    private float[]? ComposeAcceptedHeights(ComparedVersion[] compared, VertexAcceptance[] vertices, float[]? shown)
    {
        var heights = new float[CellTerrain.VertexCount];
        for (var vertex = 0; vertex < heights.Length; vertex++)
        {
            if ((AcceptedHeightAt(compared, vertices[vertex], vertex) ?? shown?[vertex]) is not { } height) return null;
            heights[vertex] = height;
        }
        return heights;
    }

    private float? AcceptedHeightAt(ComparedVersion[] compared, VertexAcceptance acceptance, int vertex) =>
        acceptance.Height.Accepted is { } accepted ? compared[accepted].Heights?[vertex] : null;

    /// <returns>Null when no vertex changed.</returns>
    private VertexChange[]? ChangesOf(ComparedVersion[] compared, VertexAcceptance[] vertices)
    {
        VertexChange[]? changes = null;
        for (var vertex = 0; vertex < vertices.Length; vertex++)
        {
            var change = new VertexChange(
                settings.CountHeightChanges ? FindChangerOf(compared, vertices[vertex].Height) : null,
                FindChangerOf(compared, vertices[vertex].Type));
            if (!change.Any) continue;
            changes ??= new VertexChange[CellTerrain.VertexCount];
            changes[vertex] = change;
        }
        return changes;
    }

    /// <summary>
    /// A property without accepted version is not checked. When the shown value differs from the accepted one and no version but a
    /// transparent one holds it, that version is the patch the game shows: an earlier patcher of the group made the change.
    /// </summary>
    /// <returns>
    /// The load-order position of the plugin named for the change, <see cref="TerrainPlugins.PatchOnlyChange"/> when only the patch
    /// holds it; null when the property is not checked or unchanged.
    /// </returns>
    private int? FindChangerOf(ComparedVersion[] compared, PropertyAcceptance property) =>
        property is { Accepted: not null, Changed: true }
            ? property.Changing is { } changing ? plugins.PositionOf(compared[changing].Plugin) : TerrainPlugins.PatchOnlyChange
            : null;

    /// <returns>Null when every vertex is checked.</returns>
    private bool[]? FindUnchecked(VertexAcceptance[] vertices) =>
        vertices.All(vertex => vertex.IsChecked) ? null : [.. vertices.Select(vertex => !vertex.IsChecked)];

    /// <param name="Type">Never changed when terrain type changes are not counted: the versions then hold no types.</param>
    private readonly record struct VertexAcceptance(PropertyAcceptance Height, PropertyAcceptance Type)
    {
        /// <summary>The vertex has an accepted height or terrain type.</summary>
        public bool IsChecked => Height.Accepted != null || Type.Accepted != null;
    }
}
