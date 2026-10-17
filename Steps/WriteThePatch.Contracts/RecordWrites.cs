using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover.Steps.WriteThePatch.Contracts;

/// <summary>A kept marker the patch moved, with the position it was written at.</summary>
public readonly record struct RecordMove(RecordKey Record, Vector3 To);

/// <summary>
/// The records one run wrote, by record. Sinking a removed object below the world is part of its removal, not a move.
/// </summary>
/// <param name="Disabled">The removed target objects, in the order they were written.</param>
/// <param name="Moved">The kept markers that were moved.</param>
public sealed record RecordWrites(ImmutableArray<RecordKey> Disabled, ImmutableArray<RecordMove> Moved);
