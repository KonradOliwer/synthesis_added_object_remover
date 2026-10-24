using System.Numerics;

namespace AddedObjectRemover;

/// <summary>A neutral instruction for the plugin writer; it says what to write, not why.</summary>
public abstract record WriteOrder(RecordKey Record);

public sealed record SetInitiallyDisabled(RecordKey Record) : WriteOrder(Record);

public sealed record SetPosition(RecordKey Record, Vector3 Position) : WriteOrder(Record);

/// <summary>Sets the position, the rotation and the scale.</summary>
/// <param name="IntoCell">
/// The cell, in the same space, whose child list of the same kind (persistent or temporary) the override goes into; null for the
/// cell the record is in now. The record must not be written into another cell by any other order.
/// </param>
public sealed record SetPlacement(RecordKey Record, EulerPlacement Placement, RecordKey? IntoCell) : WriteOrder(Record);

/// <param name="Opposite">The record's enable state is the opposite of the parent's.</param>
public sealed record SetEnableParent(RecordKey Record, RecordKey Parent, bool Opposite) : WriteOrder(Record);
