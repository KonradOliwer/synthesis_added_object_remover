using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>A load-order record and where its override is written.</summary>
internal sealed record PlacedRecordHandle(IPlacedGetter Record, PlacedRecordLocation Location);

/// <summary>
/// The plugin tool's record map: the load-order records of the target plugin that can be written, by <see cref="RecordKey"/>.
/// Only the plugin tool reads it.
/// </summary>
internal sealed class RecordHandles(IReadOnlyDictionary<RecordKey, PlacedRecordHandle> handles)
{
    public IPlacedGetter RecordOf(RecordKey key) => handles[key].Record;

    public PlacedRecordLocation LocationOf(RecordKey key) => handles[key].Location;
}
