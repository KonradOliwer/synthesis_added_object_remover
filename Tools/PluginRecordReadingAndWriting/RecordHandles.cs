using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>A load-order record and where its override is written.</summary>
internal sealed record PlacedRecordHandle(IPlacedGetter Record, PlacedRecordLocation Location);

/// <summary>
/// The plugin tool's record map: the winning load-order records of the target spaces, which can be written, by <see cref="RecordKey"/>.
/// Only the plugin tool reads it.
/// </summary>
internal sealed class RecordHandles(
    IReadOnlyDictionary<RecordKey, PlacedRecordHandle> handles, IReadOnlyDictionary<RecordKey, IPlacedGetter> earlierRecords) : IWritableRecords
{
    public IPlacedGetter RecordOf(RecordKey key) => handles[key].Record;

    /// <summary>The version of the record just below the target plugin's, for a record the target plugin overrides. A writer copies it; it never changes it.</summary>
    public IPlacedGetter EarlierRecordOf(RecordKey key) => earlierRecords[key];

    public PlacedRecordLocation LocationOf(RecordKey key) => handles[key].Location;
}
