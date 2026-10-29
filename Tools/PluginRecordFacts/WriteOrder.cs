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
/// Puts the record into the temporary children of the given cell. It comes before the record's other orders, so the record is
/// never written into the cell it leaves.
/// </summary>
public sealed record PlaceInCell(RecordKey Record, RecordKey Cell) : WriteOrder(Record);

/// <summary>Makes the record a copy of the version just below the target plugin's, whole.</summary>
public sealed record CopyVersion(RecordKey Record) : WriteOrder(Record);

/// <param name="Opposite">The record's enable state is the opposite of the parent's.</param>
public sealed record SetEnableParent(RecordKey Record, RecordKey Parent, bool Opposite) : WriteOrder(Record);
