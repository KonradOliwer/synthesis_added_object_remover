using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover.Steps.WriteThePatch.Contracts;

/// <summary>
/// What the earlier runs of one batch wrote, by record. It is built in run order and never changed, so a later run
/// decides the same whatever the threads or timing.
/// </summary>
/// <param name="Disabled">The records an earlier run removed.</param>
/// <param name="MovedTo">The records an earlier run moved, with their newest position.</param>
public sealed record WrittenByEarlierRuns(ImmutableHashSet<RecordKey> Disabled, ImmutableDictionary<RecordKey, Vector3> MovedTo)
{
    public static WrittenByEarlierRuns None { get; } = new([], ImmutableDictionary<RecordKey, Vector3>.Empty);

    public WrittenByEarlierRuns With(RecordWrites writes) => new(
        Disabled.Union(writes.Disabled),
        MovedTo.SetItems(writes.Moved.Select(move => KeyValuePair.Create(move.Record, move.To))));
}
