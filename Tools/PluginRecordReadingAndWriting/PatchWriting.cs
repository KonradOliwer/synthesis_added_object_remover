using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Writes override records into the patch plugin; the cells they go into come from the Synthesis cache.</summary>
public sealed class PatchWriting(SynthesisWriteCache synthesis, ISkyrimMod patchMod) : IPatchWriting
{
    private const string ForeignRecordsMessage = "The records to write were not read by this plugin tool.";

    /// <remarks>The writers share one set of cell overrides, so a cell is overridden once, whichever writer asks first.</remarks>
    public void Write(IWritableRecords records, IReadOnlyList<WriteOrder> orders)
    {
        var handles = HandlesOf(records);
        var cells = new CellOverrides(synthesis, patchMod);
        var placed = new PlacedRecordWriter(new PlacedOverrideWriter(cells, handles.Parts), handles);
        var cellParts = new CellPartWriter(cells, handles);
        var lands = new LandWriter(synthesis, cells, handles);
        var navmeshes = new NavmeshWriter(cells, handles);
        var maps = new NavmeshMapWriter(patchMod, handles);
        foreach (var order in orders)
        {
            switch (order)
            {
                case CopyEarlierCellParts copy: cellParts.CopyEarlierParts(copy.Record, copy.Parts); break;
                case CopyEarlierLandParts copy: lands.CopyEarlierParts(copy.Record, copy.Colours, copy.Quarters, copy.Rest); break;
                case SetLandHeights set: lands.SetHeights(set.Record, set.Heights, set.Restored, set.Beyond); break;
                case CopyEarlierNavmeshGeometry copy: navmeshes.CopyEarlierGeometry(copy.Record); break;
                case CopyEarlierNavmesh copy: navmeshes.CopyEarlier(copy.Record); break;
                case FlattenAndDetachNavmesh flatten: navmeshes.FlattenAndDetach(flatten.Record, flatten.Z); break;
                case WriteNavmeshMapEntries entries: maps.WriteEntries(entries.Record, entries.Entries); break;
                default: placed.Write(order); break;
            }
        }
    }

    public INavmeshWorldView ReadFinalNavmeshes(IWritableRecords records) => new FinalNavmeshWorld(patchMod, HandlesOf(records), synthesis);

    /// <remarks>The trial plugin has the patch's name, so the overrides it gets match those the patch would get.</remarks>
    public NavmeshFault? FindNavmeshFaultIfWritten(IWritableRecords records, IReadOnlyList<WriteOrder> orders) =>
        MutagenCalls.Read(() => FindNavmeshFaultInTrialPlugin(records, orders));

    private NavmeshFault? FindNavmeshFaultInTrialPlugin(IWritableRecords records, IReadOnlyList<WriteOrder> orders)
    {
        var trial = new SkyrimMod(patchMod.ModKey, patchMod.SkyrimRelease);
        new PatchWriting(synthesis, trial).Write(records, orders);
        return NavmeshWriteConsistency.FirstFault(orders, new HashSet<RecordKey>(), () => new FinalNavmeshWorld(trial, HandlesOf(records), synthesis));
    }

    private static RecordHandles HandlesOf(IWritableRecords records) =>
        records as RecordHandles ?? throw new ArgumentException(ForeignRecordsMessage, nameof(records));

    public void WritePatchMarker() => patchMod.ModHeader.Description = PatchMarker.AddTo(patchMod.ModHeader.Description);
}
