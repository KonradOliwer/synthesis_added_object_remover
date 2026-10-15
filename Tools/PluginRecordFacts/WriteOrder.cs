using System.Numerics;

namespace AddedObjectRemover;

/// <summary>A neutral instruction for the plugin writer; it says what to write, not why.</summary>
public abstract record WriteOrder(RecordKey Record);

public sealed record SetInitiallyDisabled(RecordKey Record) : WriteOrder(Record);

public sealed record SetPosition(RecordKey Record, Vector3 Position) : WriteOrder(Record);

/// <param name="Opposite">The record's enable state is the opposite of the parent's.</param>
public sealed record SetEnableParent(RecordKey Record, RecordKey Parent, bool Opposite) : WriteOrder(Record);
