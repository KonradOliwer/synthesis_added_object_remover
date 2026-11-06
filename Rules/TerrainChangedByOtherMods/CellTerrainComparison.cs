using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

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
    /// <param name="restored">The parts given back of the cell's records; only those of its LAND record are read.</param>
    /// <returns>
    /// The ground as the game shows it (<see cref="CellTerrain.ShownOnly"/>) when no vertex has an accepted height or terrain type,
    /// so that its water can still be compared. Null when the cell has no version, or no vertex is checked and the shown version holds
    /// no heights.
    /// </returns>
    public CellTerrain? Compare(TerrainVersion[] versions, IReadOnlyCollection<RestoredPart> restored)
    {
        if (versions.Length == 0) return null;
        var compared = versions.Select(ToCompared).ToArray();
        var trusts = versions.Select(version => plugins.TrustOf(version.Plugin)).ToArray();
        var givenBack = new GivenBackVersions(versions, plugins.Patch, restored, (one, other, vertex) => _difference.TypeDiffersAt(compared[one], compared[other], vertex));
        var vertices = Enumerable.Range(0, TerrainGrid.VertexCount).Select(vertex => DecideAt(compared, trusts, givenBack, vertex)).ToArray();
        var shown = compared[^1].Heights;
        if (!vertices.Any(vertex => vertex.IsChecked)) return shown is null ? null : CellTerrain.OfShownGround(shown);

        return new CellTerrain(
            ComposeAcceptedHeights(compared, vertices, shown),
            shown,
            ChangesOf(compared, vertices),
            FindUnchecked(vertices),
            FindTypeUnchecked(vertices),
            ShownOnly: false);
    }

    private ComparedVersion ToCompared(TerrainVersion version) =>
        new(version.Plugin, version.Heights, settings.CountTerrainTypeChanges ? types.AtEveryVertex(version) : null);

    /// <remarks>The height is decided also when height changes are not counted: the accepted heights still say where the ground was.</remarks>
    private VertexAcceptance DecideAt(ComparedVersion[] compared, VersionTrust[] trusts, GivenBackVersions givenBack, int vertex)
    {
        var properties = AcceptedVersions.Decide(
            trusts,
            [
                (one, other) => _difference.HeightDiffersAt(compared[one], compared[other], vertex),
                (one, other) => _difference.TypeDiffersAt(compared[one], compared[other], vertex),
            ],
            [givenBack.HeightAt(vertex), givenBack.TypeAt(vertex)]);
        return new VertexAcceptance(properties[HeightProperty], properties[TypeProperty]);
    }

    /// <summary>
    /// Each vertex's height from its accepted version. Where that version holds no heights, or the vertex has no accepted height, the
    /// shown height stands in: the ground there is taken as the game shows it.
    /// </summary>
    /// <returns>Null when a vertex needs the shown height and the shown version holds none.</returns>
    private float[]? ComposeAcceptedHeights(ComparedVersion[] compared, VertexAcceptance[] vertices, float[]? shown)
    {
        var heights = new float[TerrainGrid.VertexCount];
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
            changes ??= new VertexChange[TerrainGrid.VertexCount];
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

    /// <returns>Null when every vertex has an accepted terrain type.</returns>
    private bool[]? FindTypeUnchecked(VertexAcceptance[] vertices) =>
        vertices.All(vertex => vertex.Type.Accepted != null) ? null : [.. vertices.Select(vertex => vertex.Type.Accepted == null)];

    /// <summary>The versions whose values the patch's version of the LAND record was given back, per vertex, as the height and as the terrain type.</summary>
    /// <remarks>
    /// A height counts as given back only at the grid points the restore gave back, and there only where the patch still holds that
    /// height. A quarter's texture layers go back as a whole, but a vertex's terrain type counts as given back only where the restore
    /// changed it: where the type of the version of the cleaned mod of that run differs from the type of the version it went back to.
    /// A vertex of the quarter the cleaned mod left alone keeps the type it had before, given back or not. The type is made of the
    /// layers of every quarter holding the vertex, so it counts as given back from a version only when every one of those quarters was.
    /// </remarks>
    private sealed class GivenBackVersions
    {
        private readonly (PointSet Points, int[] Versions)[] _heights;
        private readonly (int Source, int CleanedMod)[][] _quarters;
        private readonly Func<int, int, int, bool> _typeDiffersAt;

        /// <param name="typeDiffersAt">Whether the terrain types of two versions differ at a vertex, given the versions first.</param>
        public GivenBackVersions(TerrainVersion[] versions, PluginName patch, IReadOnlyCollection<RestoredPart> restored, Func<int, int, int, bool> typeDiffersAt)
        {
            PluginName[] plugins = [.. versions.Select(static version => version.Plugin)];
            _typeDiffersAt = typeDiffersAt;
            _heights =
            [
                .. restored
                    .Where(static part => part.Part is RestoredPartName.LandHeights)
                    .Select(part => (((RestoredPartName.LandHeights)part.Part).Points, AcceptedVersions.GivenBackFrom(plugins, patch, [part.Source]))),
            ];
            _quarters = [.. Enumerable.Range(0, TerrainQuadrants.Count).Select(quarter => QuarterGivenBack(plugins, patch, restored, quarter))];
        }

        /// <returns>Each version the quarter was given back from, with the version of the cleaned mod of the run that gave it back; none whose cleaned mod has no version here.</returns>
        private static (int Source, int CleanedMod)[] QuarterGivenBack(PluginName[] plugins, PluginName patch, IReadOnlyCollection<RestoredPart> restored, int quarter) =>
        [
            .. restored
                .Where(part => part.Part == new RestoredPartName.LandTextureQuarter(quarter))
                .SelectMany(part => AcceptedVersions.GivenBackFrom(plugins, patch, [part.Source]).Select(source => (Source: source, CleanedMod: Array.LastIndexOf(plugins, part.CleanedMod))))
                .Where(static given => given.CleanedMod > given.Source)
                .Distinct(),
        ];

        public int[] HeightAt(int vertex) => [.. _heights.Where(heights => heights.Points.Contains(vertex)).SelectMany(static heights => heights.Versions).Distinct()];

        public int[] TypeAt(int vertex) =>
            _quarters.All(static quarter => quarter.Length == 0)
                ? []
                : TerrainQuadrants.Holding(vertex).Select(quarter => ChangedTypeSourcesAt(quarter, vertex)).Aggregate(static (all, quarter) => all.Intersect(quarter)).ToArray();

        private IEnumerable<int> ChangedTypeSourcesAt(int quarter, int vertex) =>
            _quarters[quarter].Where(given => _typeDiffersAt(given.CleanedMod, given.Source, vertex)).Select(static given => given.Source).Distinct();
    }

    /// <param name="Type">Never changed when terrain type changes are not counted: the versions then hold no types.</param>
    private readonly record struct VertexAcceptance(PropertyAcceptance Height, PropertyAcceptance Type)
    {
        /// <summary>The vertex has an accepted height or terrain type.</summary>
        public bool IsChecked => Height.Accepted != null || Type.Accepted != null;
    }
}
