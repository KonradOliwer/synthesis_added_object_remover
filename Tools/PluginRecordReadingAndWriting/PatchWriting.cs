using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Writes override records into the patch plugin; the cells they go into come from the Synthesis cache.</summary>
public sealed class PatchWriting(SynthesisWriteCache synthesis, ISkyrimMod patchMod) : IPatchWriting
{
    private const string ForeignRecordsMessage = "The records to write were not read by this plugin tool.";

    public void Write(IWritableRecords records, IReadOnlyList<WriteOrder> orders)
    {
        var handles = records as RecordHandles ?? throw new ArgumentException(ForeignRecordsMessage, nameof(records));
        new PlacedRecordWriter(new PlacedOverrideWriter(synthesis, patchMod), handles).Write(orders);
    }

    /// <summary>Marks the patch as this patcher's output in its header description (<see cref="PatchMarker"/>).</summary>
    public void WritePatchMarker() => patchMod.ModHeader.Description = PatchMarker.AddTo(patchMod.ModHeader.Description);
}
