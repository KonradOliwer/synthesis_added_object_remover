using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// The unreadable records already named in the log of a Synthesis run, so the configs of a batch, which run one after the
/// other in the batch's order, name a record once: in the first config that reads it.
/// </summary>
public sealed class UnreadableRecordsReported
{
    private readonly HashSet<RecordKey> _reported = [];

    /// <returns>The records not named before, in the order given; they count as named from now on.</returns>
    public ImmutableArray<UnreadableRecord> TakeNew(IEnumerable<UnreadableRecord> records) =>
        [.. records.Where(record => _reported.Add(record.Key))];
}
