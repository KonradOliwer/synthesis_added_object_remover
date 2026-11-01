using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>The rules of the navmesh consistency check, in the order they are checked.</summary>
public enum NavmeshRule
{
    /// <summary>
    /// V1: the navmesh resolves and is not deleted, lies in a cell that is not deleted and that its parent names, and has a map entry
    /// naming the same parent.
    /// </summary>
    Identity,

    /// <summary>V2: vertex indices, inner edges and edge-link entries are in range.</summary>
    Triangles,

    /// <summary>V3: each edge link names a triangle that links back over an edge whose vertices coincide.</summary>
    Links,

    /// <summary>V4: door triangles and the navmesh links of the doors that stand name each other; door triangles brought back name only doors that stand.</summary>
    Doors,

    /// <summary>V5: the bounds hold every vertex, and the grid comes from a version with as many triangles.</summary>
    Spatial,

    /// <summary>V6: the navmeshes and doors the map entry and the preferred paths name exist.</summary>
    PathData,
}

/// <summary>How a navmesh breaks a rule; the fault's index and other record say where.</summary>
public enum NavmeshFaultKind
{
    /// <summary>The navmesh does not resolve.</summary>
    Missing,

    /// <summary>The navmesh is deleted.</summary>
    Deleted,

    /// <summary>The cell holding the navmesh is deleted.</summary>
    CellDeleted,

    /// <summary>The navmesh has no navmesh data.</summary>
    NoData,

    /// <summary>Its data names another cell as its parent than the one holding it.</summary>
    ParentIsAnotherCell,

    /// <summary>No navmesh info map carries an entry for it.</summary>
    NoMapEntry,

    /// <summary>Its map entry names another parent than its data.</summary>
    MapEntryNamesAnotherParent,

    /// <summary>A triangle names a vertex out of range.</summary>
    VertexOutOfRange,

    /// <summary>A triangle's inner edge names a triangle out of range.</summary>
    InnerEdgeOutOfRange,

    /// <summary>A triangle's edge-link flag selects no entry, or one past the edge-link table.</summary>
    EdgeLinkEntryOutOfRange,

    /// <summary>A triangle links to a navmesh that does not resolve or is deleted.</summary>
    LinkedNavmeshMissing,

    /// <summary>A triangle links to a triangle out of range of the other navmesh.</summary>
    LinkedTriangleOutOfRange,

    /// <summary>The triangle linked to has no link back.</summary>
    NoLinkBack,

    /// <summary>The triangle linked to links back, but the shared edge's vertices lie apart.</summary>
    SharedEdgeApart,

    /// <summary>A door triangle is out of range.</summary>
    DoorTriangleOutOfRange,

    /// <summary>A door triangle names a door that does not resolve or is deleted.</summary>
    DoorMissing,

    /// <summary>A door triangle brought back names a door that does not stand.</summary>
    DoorDoesNotStand,

    /// <summary>A door triangle names a door whose navmesh link names another navmesh or triangle, or none.</summary>
    DoorLinksElsewhere,

    /// <summary>A door's navmesh link names a triangle without a door triangle for that door.</summary>
    DoorLinkWithoutDoorTriangle,

    /// <summary>A vertex lies outside the bounds.</summary>
    VertexOutsideBounds,

    /// <summary>No version read whose grid the navmesh holds has as many triangles.</summary>
    GridFromAnotherTriangleCount,

    /// <summary>The map entry merges it with a navmesh that does not exist.</summary>
    MergedNavmeshMissing,

    /// <summary>The map entry names a door that does not resolve or is deleted.</summary>
    LinkedDoorMissing,

    /// <summary>A preferred path set holding it names a navmesh that does not exist.</summary>
    PreferredPathNavmeshMissing,
}

/// <summary>The first rule a navmesh breaks, and how.</summary>
/// <param name="Index">The triangle, or for <see cref="NavmeshFaultKind.VertexOutsideBounds"/> the vertex; null when no single one is at fault.</param>
/// <param name="Other">The other navmesh or door at fault; null when none is.</param>
public sealed record NavmeshFault(RecordKey Navmesh, NavmeshRule Rule, NavmeshFaultKind Kind, int? Index, RecordKey? Other);

/// <summary>Where a navmesh lies: an interior cell, or a worldspace and the grid of its cell.</summary>
/// <param name="Space">The interior cell, or the worldspace.</param>
/// <param name="Grid">Null for an interior.</param>
public readonly record struct NavmeshPlace(RecordKey Space, CellGridPoint? Grid);

/// <summary>One edge of a navmesh triangle.</summary>
/// <param name="IsEdgeLink">The edge leads into another navmesh.</param>
/// <param name="Index">
/// For an edge link, the entry of the navmesh's edge-link table; otherwise the neighbouring triangle in the same navmesh. Null when
/// the edge names none.
/// </param>
public readonly record struct NavmeshEdgeView(bool IsEdgeLink, int? Index);

/// <param name="Corners">The three corners, as indices into the navmesh's vertices.</param>
/// <param name="Edges">The edges from corner 0 to 1, 1 to 2 and 2 to 0.</param>
public sealed record NavmeshTriangleView(ImmutableArray<int> Corners, ImmutableArray<NavmeshEdgeView> Edges);

/// <summary>An entry of a navmesh's edge-link table: the triangle of another navmesh an edge leads into.</summary>
public readonly record struct NavmeshLinkTarget(RecordKey Navmesh, int Triangle);

/// <summary>A navmesh as a world holds it.</summary>
/// <param name="HeldIn">The cell whose navmesh list holds it.</param>
/// <param name="HeldInDeletedCell">The cell record holding it is deleted.</param>
/// <param name="Parent">The cell its navmesh data names as its parent; null when it has no navmesh data.</param>
/// <param name="Vertices">In world space.</param>
/// <param name="GridSourceTriangleCounts">The triangle counts of the versions read whose navmesh grid this navmesh holds.</param>
public sealed record NavmeshView(
    bool IsDeleted,
    NavmeshPlace HeldIn,
    bool HeldInDeletedCell,
    NavmeshPlace? Parent,
    ImmutableArray<Vector3> Vertices,
    ImmutableArray<NavmeshTriangleView> Triangles,
    ImmutableArray<NavmeshLinkTarget> EdgeLinks,
    ImmutableArray<NavmeshDoorTriangleFact> DoorTriangles,
    Vector3 Min,
    Vector3 Max,
    ImmutableArray<int> GridSourceTriangleCounts);

/// <summary>A navmesh's entry in the navmesh info map, merged over the plugins that carry one.</summary>
public sealed record NavmeshEntryView(NavmeshPlace Parent, ImmutableArray<RecordKey> MergedTo, ImmutableArray<RecordKey> PreferredMerges, ImmutableArray<RecordKey> LinkedDoors);

/// <summary>A placed record as a world holds it, with its navmesh link (XNDP); null when it has none.</summary>
public sealed record DoorView(bool IsDeleted, NavDoorLinkFact? Link);

/// <summary>The navmeshes, navmesh info map and doors of one world, as the consistency check reads them.</summary>
public interface INavmeshWorldView
{
    /// <returns>Null when the world holds no such navmesh.</returns>
    NavmeshView? NavmeshOf(RecordKey navmesh);

    /// <summary>The navmesh resolves anywhere in the world and is not deleted.</summary>
    bool Exists(RecordKey navmesh);

    /// <returns>Null when no plugin's map carries an entry for the navmesh.</returns>
    NavmeshEntryView? EntryOf(RecordKey navmesh);

    /// <returns>Null when the world holds no such placed record.</returns>
    DoorView? DoorOf(RecordKey door);

    /// <summary>The navmeshes whose edge-link table names the navmesh.</summary>
    IEnumerable<RecordKey> NavmeshesLinkingTo(RecordKey navmesh);

    /// <summary>The doors whose navmesh link names the navmesh.</summary>
    IEnumerable<RecordKey> DoorsLinkingTo(RecordKey navmesh);

    /// <summary>The navmesh sets of the preferred paths (NVPP) in force.</summary>
    IEnumerable<ImmutableArray<RecordKey>> PreferredPathSets { get; }
}

/// <summary>
/// Checks that navmeshes fit together as the game reads them: each navmesh named, and each navmesh an edge links from or into one
/// of them, against rules V1–V6 (<see cref="NavmeshRule"/>). It knows nothing of which plugin changed what.
/// </summary>
public static class NavmeshConsistency
{
    private const float VertexTolerance = PlacedPartTolerances.ResavedPosition;

    private const int CornerCount = 3;

    /// <param name="broughtBack">The named navmeshes whose triangles were brought back from an earlier version.</param>
    /// <param name="doorsThatDoNotStand">
    /// Placed records removed although the world does not mark them deleted. Like a deleted door, one that does not stand has its
    /// navmesh link checked neither way, and a door triangle brought back may not name it.
    /// </param>
    /// <returns>The first fault, navmesh by navmesh in key order and rule by rule; null when every checked navmesh passes.</returns>
    public static NavmeshFault? FirstFault(
        INavmeshWorldView world, IReadOnlyCollection<RecordKey> navmeshes, IReadOnlySet<RecordKey> broughtBack, IReadOnlySet<RecordKey> doorsThatDoNotStand)
    {
        foreach (var key in NavmeshesToCheck(world, navmeshes))
        {
            if (FirstFaultOf(world, broughtBack.Contains(key), doorsThatDoNotStand, key) is { } fault) return fault;
        }
        return null;
    }

    /// <summary>
    /// The named navmeshes, and every navmesh that resolves, is not deleted and an edge link leads to or comes from, in key order. A
    /// link into a missing or deleted navmesh is the linking navmesh's fault.
    /// </summary>
    private static IEnumerable<RecordKey> NavmeshesToCheck(INavmeshWorldView world, IReadOnlyCollection<RecordKey> navmeshes)
    {
        var toCheck = new HashSet<RecordKey>(navmeshes);
        foreach (var key in navmeshes)
        {
            var linkedFrom = world.NavmeshOf(key)?.EdgeLinks.Select(link => link.Navmesh) ?? [];
            toCheck.UnionWith(linkedFrom.Concat(world.NavmeshesLinkingTo(key)).Where(linked => world.NavmeshOf(linked) is { IsDeleted: false }));
        }
        return toCheck.Order(RecordKeyOrder.Comparer);
    }

    private static NavmeshFault? FirstFaultOf(INavmeshWorldView world, bool broughtBack, IReadOnlySet<RecordKey> doorsThatDoNotStand, RecordKey key)
    {
        if (IdentityFault(world, key) is { } identity) return identity;
        var navmesh = world.NavmeshOf(key)!;
        return TriangleFault(key, navmesh)
            ?? LinkFault(world, key, navmesh)
            ?? DoorFault(world, broughtBack, doorsThatDoNotStand, key, navmesh)
            ?? SpatialFault(key, navmesh)
            ?? PathDataFault(world, key, world.EntryOf(key)!);
    }

    private static NavmeshFault? IdentityFault(INavmeshWorldView world, RecordKey key)
    {
        var kind = world.NavmeshOf(key) switch
        {
            null => NavmeshFaultKind.Missing,
            { IsDeleted: true } => NavmeshFaultKind.Deleted,
            { HeldInDeletedCell: true } => NavmeshFaultKind.CellDeleted,
            { Parent: null } => NavmeshFaultKind.NoData,
            var navmesh when navmesh.Parent != navmesh.HeldIn => NavmeshFaultKind.ParentIsAnotherCell,
            var navmesh => world.EntryOf(key) switch
            {
                null => NavmeshFaultKind.NoMapEntry,
                var entry when entry.Parent != navmesh.Parent => NavmeshFaultKind.MapEntryNamesAnotherParent,
                _ => (NavmeshFaultKind?)null,
            },
        };
        return kind is { } broken ? new NavmeshFault(key, NavmeshRule.Identity, broken, null, null) : null;
    }

    private static NavmeshFault? TriangleFault(RecordKey key, NavmeshView navmesh)
    {
        for (var index = 0; index < navmesh.Triangles.Length; index++)
        {
            var triangle = navmesh.Triangles[index];
            var kind = triangle.Corners.Any(corner => !IsIndex(corner, navmesh.Vertices.Length))
                ? NavmeshFaultKind.VertexOutOfRange
                : triangle.Edges.Select(edge => EdgeFault(navmesh, edge)).FirstOrDefault(fault => fault is not null);
            if (kind is { } broken) return new NavmeshFault(key, NavmeshRule.Triangles, broken, index, null);
        }
        return null;
    }

    private static NavmeshFaultKind? EdgeFault(NavmeshView navmesh, NavmeshEdgeView edge) => edge switch
    {
        { IsEdgeLink: true, Index: var entry } when entry is not { } inTable || !IsIndex(inTable, navmesh.EdgeLinks.Length) => NavmeshFaultKind.EdgeLinkEntryOutOfRange,
        { IsEdgeLink: false, Index: { } neighbour } when !IsIndex(neighbour, navmesh.Triangles.Length) => NavmeshFaultKind.InnerEdgeOutOfRange,
        _ => null,
    };

    private static NavmeshFault? LinkFault(INavmeshWorldView world, RecordKey key, NavmeshView navmesh)
    {
        for (var index = 0; index < navmesh.Triangles.Length; index++)
        {
            var triangle = navmesh.Triangles[index];
            for (var edge = 0; edge < CornerCount; edge++)
            {
                if (triangle.Edges[edge] is not { IsEdgeLink: true, Index: { } entry }) continue;
                var target = navmesh.EdgeLinks[entry];
                if (OneLinkFault(world, new NavmeshLinkTarget(key, index), EdgeEnds(navmesh, triangle, edge), target) is { } kind)
                    return new NavmeshFault(key, NavmeshRule.Links, kind, index, target.Navmesh);
            }
        }
        return null;
    }

    /// <param name="from">The navmesh and triangle the link leaves.</param>
    /// <param name="ends">The two vertices of the edge it leaves over.</param>
    private static NavmeshFaultKind? OneLinkFault(INavmeshWorldView world, NavmeshLinkTarget from, (Vector3 From, Vector3 To) ends, NavmeshLinkTarget target)
    {
        if (world.NavmeshOf(target.Navmesh) is not { IsDeleted: false } other) return NavmeshFaultKind.LinkedNavmeshMissing;
        if (!IsIndex(target.Triangle, other.Triangles.Length)) return NavmeshFaultKind.LinkedTriangleOutOfRange;
        var triangle = other.Triangles[target.Triangle];
        var linksBack = Enumerable.Range(0, CornerCount)
            .Where(edge => triangle.Edges[edge] is { IsEdgeLink: true, Index: { } entry } && IsIndex(entry, other.EdgeLinks.Length) && other.EdgeLinks[entry] == from)
            .ToList();
        if (linksBack.Count == 0) return NavmeshFaultKind.NoLinkBack;
        return linksBack.Any(edge => EdgesCoincide(ends, EdgeEnds(other, triangle, edge))) ? null : NavmeshFaultKind.SharedEdgeApart;
    }

    /// <summary>
    /// A door triangle still names a door that resolves and is not deleted, and one brought back a door that stands; only a door that
    /// stands has its link checked, either way.
    /// </summary>
    /// <remarks>
    /// A door that does not stand may stay named by a door triangle the world already had: the game keeps such triangles for doors
    /// a quest enables later.
    /// </remarks>
    private static NavmeshFault? DoorFault(INavmeshWorldView world, bool broughtBack, IReadOnlySet<RecordKey> doorsThatDoNotStand, RecordKey key, NavmeshView navmesh)
    {
        foreach (var (triangle, door) in navmesh.DoorTriangles)
        {
            var kind = !IsIndex(triangle, navmesh.Triangles.Length) ? NavmeshFaultKind.DoorTriangleOutOfRange
                : world.DoorOf(door) is not { IsDeleted: false } placed ? NavmeshFaultKind.DoorMissing
                : doorsThatDoNotStand.Contains(door) ? (broughtBack ? NavmeshFaultKind.DoorDoesNotStand : (NavmeshFaultKind?)null)
                : placed.Link != new NavDoorLinkFact(key, triangle) ? NavmeshFaultKind.DoorLinksElsewhere
                : (NavmeshFaultKind?)null;
            if (kind is { } broken) return new NavmeshFault(key, NavmeshRule.Doors, broken, triangle, door);
        }
        foreach (var door in world.DoorsLinkingTo(key).Where(door => !doorsThatDoNotStand.Contains(door) && world.DoorOf(door) is { IsDeleted: false }).Order(RecordKeyOrder.Comparer))
        {
            var link = world.DoorOf(door)!.Link!.Value;
            if (!navmesh.DoorTriangles.Contains(new NavmeshDoorTriangleFact(link.Triangle, door)))
                return new NavmeshFault(key, NavmeshRule.Doors, NavmeshFaultKind.DoorLinkWithoutDoorTriangle, link.Triangle, door);
        }
        return null;
    }

    private static NavmeshFault? SpatialFault(RecordKey key, NavmeshView navmesh)
    {
        var low = navmesh.Min - new Vector3(VertexTolerance);
        var high = navmesh.Max + new Vector3(VertexTolerance);
        for (var index = 0; index < navmesh.Vertices.Length; index++)
        {
            var vertex = navmesh.Vertices[index];
            if (Vector3.Min(vertex, low) != low || Vector3.Max(vertex, high) != high) return new NavmeshFault(key, NavmeshRule.Spatial, NavmeshFaultKind.VertexOutsideBounds, index, null);
        }
        return navmesh.GridSourceTriangleCounts.Contains(navmesh.Triangles.Length)
            ? null
            : new NavmeshFault(key, NavmeshRule.Spatial, NavmeshFaultKind.GridFromAnotherTriangleCount, null, null);
    }

    private static NavmeshFault? PathDataFault(INavmeshWorldView world, RecordKey key, NavmeshEntryView entry)
    {
        var missing = FirstNot(entry.MergedTo.Concat(entry.PreferredMerges), world.Exists) is { } merged ? (NavmeshFaultKind.MergedNavmeshMissing, merged)
            : FirstNot(entry.LinkedDoors, door => world.DoorOf(door) is { IsDeleted: false }) is { } door ? (NavmeshFaultKind.LinkedDoorMissing, door)
            : world.PreferredPathSets.Where(set => set.Contains(key)).Select(set => FirstNot(set, world.Exists)).FirstOrDefault(found => found is not null) is { } named ? (NavmeshFaultKind.PreferredPathNavmeshMissing, named)
            : ((NavmeshFaultKind, RecordKey)?)null;
        return missing is (var kind, var other) ? new NavmeshFault(key, NavmeshRule.PathData, kind, null, other) : null;
    }

    private static RecordKey? FirstNot(IEnumerable<RecordKey> keys, Func<RecordKey, bool> holds) =>
        keys.Where(key => !holds(key)).Select(key => (RecordKey?)key).FirstOrDefault();

    private static (Vector3 From, Vector3 To) EdgeEnds(NavmeshView navmesh, NavmeshTriangleView triangle, int edge) =>
        (VertexAt(navmesh, triangle.Corners[edge]), VertexAt(navmesh, triangle.Corners[(edge + 1) % CornerCount]));

    /// <summary>A corner out of range lies nowhere, so no edge through it coincides with another.</summary>
    private static Vector3 VertexAt(NavmeshView navmesh, int corner) =>
        IsIndex(corner, navmesh.Vertices.Length) ? navmesh.Vertices[corner] : new Vector3(float.NaN);

    /// <summary>Whether two edges join the same two points within <see cref="PlacedPartTolerances.ResavedPosition"/>; two navmeshes list a shared edge's ends in either order.</summary>
    public static bool EdgesCoincide((Vector3 From, Vector3 To) one, (Vector3 From, Vector3 To) other) =>
        (Near(one.From, other.From) && Near(one.To, other.To)) || (Near(one.From, other.To) && Near(one.To, other.From));

    private static bool Near(Vector3 one, Vector3 other)
    {
        var apart = Vector3.Abs(one - other);
        return apart.X <= VertexTolerance && apart.Y <= VertexTolerance && apart.Z <= VertexTolerance;
    }

    private static bool IsIndex(int value, int count) => value >= 0 && value < count;
}
