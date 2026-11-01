using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>A neutral instruction for the plugin writer; it says what to write, not why.</summary>
public abstract record WriteOrder(RecordKey Record);

public sealed record SetInitiallyDisabled(RecordKey Record) : WriteOrder(Record);

public sealed record SetPosition(RecordKey Record, Vector3 Position) : WriteOrder(Record);

/// <summary>Sets the position, the rotation and the scale.</summary>
/// <param name="Scale">The scale as the record stores it; null for a record that stores none.</param>
public sealed record SetPlacement(RecordKey Record, Vector3 Position, Vector3 Rotation, float? Scale) : WriteOrder(Record);

/// <summary>
/// Puts the record into the persistent or temporary children of the given cell; its Persistent flag follows that list. It comes
/// before the record's other orders, so the record is never written into the list it leaves.
/// </summary>
public sealed record PlaceInCell(RecordKey Record, RecordKey Cell, bool InPersistentList) : WriteOrder(Record);

/// <summary>
/// Gives each of the parts the value of the version just below the target plugin's; every other part keeps the value the game
/// shows now. A deleted record whose presence goes back becomes a whole copy of that version.
/// </summary>
public sealed record UndoPlacedParts(RecordKey Record, PlacedParts Parts) : WriteOrder(Record);

/// <summary>Removes the door's navmesh link (XNDP).</summary>
public sealed record ClearNavDoorLink(RecordKey Record) : WriteOrder(Record);

/// <summary>
/// Gives each of the parts of a cell record the value of the version just below the target plugin's; every other part keeps the
/// value the game shows now.
/// </summary>
public sealed record UndoCellParts(RecordKey Record, ImmutableArray<CellPartName> Parts) : WriteOrder(Record);

/// <summary>
/// Gives the listed parts of a LAND record the values of the version just below the target plugin's: the vertex colours of the
/// points, the texture layers of the quarters and, with <paramref name="Rest"/>, the rest of the record. It comes before the
/// record's <see cref="SetLandHeights"/>, so the heights are written into the grids the rest brings back.
/// </summary>
public sealed record UndoLandParts(RecordKey Record, PointSet Colours, QuarterSet Quarters, bool Rest) : WriteOrder(Record);

/// <summary>
/// Sets the height of every grid point of a LAND record, and the normal of each restored one: a restored point whose own height and
/// those of its neighbours in the cell all equal the version just below the target plugin's takes that version's normal; one whose
/// all equal the winner's keeps the winner's; every other restored point gets the normal of the new heights. Every point not restored
/// keeps the winner's normal, or gets the normal of the new heights when the winner holds none.
/// </summary>
/// <param name="Heights">In game units, row by row from the bottom, x fastest; they must be storable (<see cref="LandHeightEncoder"/>).</param>
/// <param name="Restored">The points whose height and normal go back.</param>
/// <param name="Beyond">The final heights just outside the cell, for the normals at its edges.</param>
public sealed record SetLandHeights(RecordKey Record, float[] Heights, PointSet Restored, CellEdgeHeights Beyond) : WriteOrder(Record);

/// <summary>The navmesh takes the geometry of its version just below the target plugin's; its deleted state and its parent stay.</summary>
public sealed record CopyNavmeshGeometry(RecordKey Record) : WriteOrder(Record);

/// <summary>The navmesh's version just below the target plugin's comes back whole and not deleted, in the cell that holds that version.</summary>
public sealed record BringBackNavmesh(RecordKey Record) : WriteOrder(Record);

/// <summary>
/// Lowers every vertex of the navmesh to <paramref name="Z"/> and cuts it off: no edge link, door, cover or preferred triangle stays.
/// X and Y, the triangles in their order, the inner edges and the grid stay; it is not deleted.
/// </summary>
public sealed record SinkNavmesh(RecordKey Record, float Z) : WriteOrder(Record);

/// <summary>
/// The patch's navmesh info map holds the given entries besides those it already holds, in navmesh key order, with the winning
/// map's preferred paths, NVSI and navmesh version.
/// </summary>
public sealed record WriteNavmeshMapEntries(RecordKey Record, ImmutableArray<NavmeshEntryOrder> Entries) : WriteOrder(Record);

/// <param name="From">The plugin whose navmesh info map carries the entry copied.</param>
/// <param name="SinkTo">
/// Null for an entry copied as it is; otherwise the depth its point and island are lowered to, and its merges and doors are emptied.
/// </param>
public sealed record NavmeshEntryOrder(RecordKey Navmesh, PluginName From, float? SinkTo);

/// <param name="Opposite">The record's enable state is the opposite of the parent's.</param>
public sealed record SetEnableParent(RecordKey Record, RecordKey Parent, bool Opposite) : WriteOrder(Record);
