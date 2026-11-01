using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>One outer edge of a navmesh triangle: the flag that marks it as an edge link, its edge value and its two corners.</summary>
internal sealed record NavmeshTriangleEdge(
    NavmeshTriangle.Flag Flag,
    Func<INavmeshTriangleGetter, short> Value,
    Action<INavmeshTriangle, short> SetValue,
    Func<P3Int16, short> From,
    Func<P3Int16, short> To);

/// <summary>
/// The three edges of a navmesh triangle. An edge whose flag is set is an edge link and its value indexes the navmesh's edge-link
/// table; otherwise its value is the index of the neighbouring triangle in the same navmesh, or <see cref="None"/>.
/// </summary>
internal static class NavmeshTriangleEdges
{
    /// <summary>The edge value of an edge with no neighbour.</summary>
    public const short None = -1;

    public static IReadOnlyList<NavmeshTriangleEdge> All { get; } =
    [
        new(NavmeshTriangle.Flag.EdgeLink_0_1, static triangle => triangle.EdgeLink_0_1, static (triangle, value) => triangle.EdgeLink_0_1 = value, static corners => corners.X, static corners => corners.Y),
        new(NavmeshTriangle.Flag.EdgeLink_1_2, static triangle => triangle.EdgeLink_1_2, static (triangle, value) => triangle.EdgeLink_1_2 = value, static corners => corners.Y, static corners => corners.Z),
        new(NavmeshTriangle.Flag.EdgeLink_2_0, static triangle => triangle.EdgeLink_2_0, static (triangle, value) => triangle.EdgeLink_2_0 = value, static corners => corners.Z, static corners => corners.X),
    ];
}
