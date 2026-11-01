using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>How the target plugin changed a navmesh of a target space.</summary>
public enum NavmeshCase
{
    /// <summary>It overrides another plugin's navmesh and does not delete it.</summary>
    Edited,

    /// <summary>It deletes another plugin's navmesh, and no navmesh it adds overlaps that navmesh's earlier triangles.</summary>
    DeletedWithoutReplacement,

    /// <summary>It deletes another plugin's navmesh, and a navmesh it adds overlaps that navmesh's earlier triangles in XY in the same cell.</summary>
    DeletedAndReplaced,

    /// <summary>It defines the navmesh itself.</summary>
    Added,
}

/// <summary>One triangle of a navmesh: its corners as indices into the navmesh's vertices, and its Preferred flag.</summary>
public readonly record struct NavmeshTriangleFact(int A, int B, int C, bool Preferred);

/// <summary>An outer edge of a navmesh triangle linked to a triangle of another navmesh.</summary>
/// <param name="Triangle">The linked triangle's index in its own navmesh.</param>
/// <param name="From">One end of the shared edge, in world space.</param>
/// <param name="To">The other end of the shared edge, in world space.</param>
public readonly record struct NavmeshEdgeLinkFact(int Triangle, RecordKey Neighbour, int NeighbourTriangle, Vector3 From, Vector3 To);

/// <summary>A navmesh triangle that leads through a door.</summary>
public readonly record struct NavmeshDoorTriangleFact(int Triangle, RecordKey Door);

/// <summary>A navmesh's entry in one plugin's navmesh info map.</summary>
/// <param name="Map">The navmesh info map record holding the entry.</param>
public sealed record NavmeshMapEntryFact(
    PluginName Plugin, RecordKey Map, ImmutableArray<RecordKey> MergedTo, ImmutableArray<RecordKey> PreferredMerges, ImmutableArray<RecordKey> LinkedDoors);

/// <summary>One version of a navmesh as its plugin stores it, with the map entry in force at that version.</summary>
/// <param name="Plugin">The plugin of the navmesh record.</param>
/// <param name="Cell">The cell record whose navmesh list holds this version.</param>
/// <param name="Vertices">In world space.</param>
/// <param name="EdgeLinks">
/// In triangle order, each triangle's edges in corner order. A link on a triangle naming a missing vertex, or whose edge value lies
/// outside the navmesh's edge-link table, is left out.
/// </param>
/// <param name="MapEntry">The entry of the last plugin up to this version's level that carries one; null when none does.</param>
public sealed record NavmeshShape(
    PluginName Plugin,
    RecordKey Cell,
    bool IsDeleted,
    ImmutableArray<Vector3> Vertices,
    ImmutableArray<NavmeshTriangleFact> Triangles,
    ImmutableArray<NavmeshEdgeLinkFact> EdgeLinks,
    ImmutableArray<NavmeshDoorTriangleFact> DoorTriangles,
    NavmeshMapEntryFact? MapEntry)
{
    /// <returns>Null when the triangle names a missing vertex.</returns>
    public MeshTriangle? CornersOf(int triangle)
    {
        var corners = Triangles[triangle];
        return IsVertex(corners.A) && IsVertex(corners.B) && IsVertex(corners.C)
            ? new MeshTriangle(Vertices[corners.A], Vertices[corners.B], Vertices[corners.C])
            : null;
    }

    /// <summary>The triangles in world space, in triangle order, but those naming a missing vertex.</summary>
    public IEnumerable<MeshTriangle> WorldTriangles() => Enumerable.Range(0, Triangles.Length).Select(CornersOf).OfType<MeshTriangle>();

    private bool IsVertex(int index) => index >= 0 && index < Vertices.Length;
}

/// <summary>A navmesh edge-linked to a navmesh the target plugin changed, in either direction and in any version read.</summary>
/// <param name="Earlier">Its version just below the target plugin's; null when it has none.</param>
public sealed record NeighbourNavmesh(RecordKey Navmesh, NavmeshShape Winner, NavmeshShape? Earlier);

/// <summary>Where a door's navmesh link (XNDP) points.</summary>
public readonly record struct NavDoorLinkFact(RecordKey Navmesh, int Triangle);

/// <summary>A door whose navmesh link names a navmesh, or that a navmesh's door triangles or map entry name, in any version read.</summary>
/// <param name="NavDoorLink">What the target plugin last changed in the door's navmesh link; <see cref="PartOutcome.Unchanged"/> when it does not override the door.</param>
/// <param name="Earlier">The link of the door's version just below the target plugin's; null when it has none or that version has no link.</param>
/// <param name="Current">The link of the door's winning version; null when it has none.</param>
/// <param name="InDeletedCell">A version read deletes the cell whose child list holds the door's winning version.</param>
public sealed record DoorLinkFact(RecordKey Door, PartOutcome NavDoorLink, NavDoorLinkFact? Earlier, NavDoorLinkFact? Current, bool InDeletedCell);

/// <summary>
/// A navmesh of a target space that the target plugin overrides, deletes or adds, with what it last changed, judged over the
/// versions from the one just below the target plugin's up to the winner, the patch included.
/// </summary>
/// <param name="Cell">The cell record whose navmesh list holds the winning version.</param>
/// <param name="Geometry">The navmesh's data and raw navmesh bytes; every record flag but Deleted.</param>
/// <param name="Presence">The Deleted flag; an added navmesh has no version below, which counts as a change.</param>
/// <param name="MapEntry">
/// Its entry in the navmesh info map: the entries of the plugins whose map carries one, in load order. <see cref="PartOutcome.Unchanged"/>
/// when the target plugin's map carries none.
/// </param>
/// <param name="Earlier">The version just below the target plugin's; null for <see cref="NavmeshCase.Added"/>.</param>
/// <param name="Current">The target plugin's version; for a deletion, the earlier version with the deleted state.</param>
/// <param name="ReplacedBy">For <see cref="NavmeshCase.DeletedAndReplaced"/>: the added navmeshes that overlap it, in key order.</param>
/// <param name="Neighbours">In key order; none when a later plugin changed a part again, since the navmesh then never goes back.</param>
/// <param name="Doors">In key order.</param>
/// <param name="NamedInPreferredPaths">The winning navmesh info map's preferred paths name it.</param>
/// <param name="WinningMapMod">The plugin of the last navmesh info map in load order; null when no plugin has one.</param>
/// <param name="CellDeletedInAVersion">A version read of <paramref name="Cell"/> deletes the cell.</param>
/// <param name="LaterWinner">The winning version when a plugin after the target plugin wins the navmesh; null when the target plugin's version wins.</param>
/// <param name="WinningMapEntry">The entry of the last plugin whose navmesh info map carries one, whoever wins the navmesh record; null when none does.</param>
/// <remarks>When a version above the target plugin's deletes a navmesh the target plugin keeps, nothing in it goes back.</remarks>
public sealed record NavmeshRecordFact(
    RecordKey Key,
    RecordKey SpaceKey,
    RecordKey Cell,
    PluginName WinningMod,
    NavmeshCase Case,
    PartOutcome Geometry,
    PartOutcome Presence,
    PartOutcome MapEntry,
    NavmeshShape? Earlier,
    NavmeshShape Current,
    ImmutableArray<RecordKey> ReplacedBy,
    ImmutableArray<NeighbourNavmesh> Neighbours,
    ImmutableArray<DoorLinkFact> Doors,
    bool NamedInPreferredPaths,
    PluginName? WinningMapMod,
    bool CellDeletedInAVersion,
    NavmeshShape? LaterWinner,
    NavmeshMapEntryFact? WinningMapEntry);
