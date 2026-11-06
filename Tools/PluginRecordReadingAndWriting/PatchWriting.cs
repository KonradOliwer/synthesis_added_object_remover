using System.Collections.Immutable;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Writes override records and new records into the patch plugin; the cells they go into come from the Synthesis cache.</summary>
/// <param name="patchMarker">The text in a plugin's header description that marks it as this patcher's output.</param>
public sealed class PatchWriting(SynthesisWriteCache synthesis, ISkyrimMod patchMod, string patchMarker) : IPatchWriting
{
    private const string ForeignRecordsMessage = "The records to write were not read by this plugin tool.";

    public ImmutableArray<RecordKey> Write(IWritableRecords records, IReadOnlyList<WriteOrder> orders) => WriteInto(patchMod, records, orders);

    public INavmeshWorldView ReadFinalNavmeshes(IWritableRecords records) => new FinalNavmeshWorld(patchMod, HandlesOf(records), synthesis);

    /// <remarks>The trial plugin has the patch's name, so the overrides it gets match those the patch would get.</remarks>
    public TResult ReadNavmeshesIfWritten<TResult>(IWritableRecords records, IReadOnlyList<WriteOrder> orders, Func<INavmeshWorldView, TResult> read)
    {
        var trial = new SkyrimMod(patchMod.ModKey, patchMod.SkyrimRelease);
        WriteInto(trial, records, orders);
        return read(new FinalNavmeshWorld(trial, HandlesOf(records), synthesis));
    }

    public void WritePatchMarker() => patchMod.ModHeader.Description = PatchMarker.AddTo(patchMod.ModHeader.Description, patchMarker);

    /// <summary>Whether the patch's header description holds the patch marker now; read before this patcher writes its own.</summary>
    public bool CarriesPatchMarker() => PatchMarker.IsIn(patchMod.ModHeader.Description, patchMarker);

    /// <remarks>The writers share one set of cell overrides, so a cell is overridden once, whichever writer asks first.</remarks>
    /// <returns>The keys of the records the orders added, in the order of their orders.</returns>
    private ImmutableArray<RecordKey> WriteInto(ISkyrimMod mod, IWritableRecords records, IReadOnlyList<WriteOrder> orders)
    {
        var handles = HandlesOf(records);
        var cells = new CellOverrides(synthesis, mod);
        var overrides = new PlacedOverrideWriter(cells, handles.Parts, mod);
        var placed = new PlacedRecordWriter(overrides, handles);
        var cellParts = new CellPartWriter(cells, handles);
        var lands = new LandWriter(synthesis, cells, handles);
        var navmeshes = new NavmeshWriter(cells, handles);
        var maps = new NavmeshMapWriter(mod, handles);
        foreach (var order in orders)
        {
            switch (order)
            {
                case CopyEarlierCellParts copy: cellParts.CopyEarlierParts(copy.Record, copy.Parts); break;
                case CopyEarlierLandParts copy: lands.CopyEarlierParts(copy.Record, copy.Colours, copy.Quarters, copy.Rest); break;
                case SetLandHeights set: lands.SetHeights(set.Record, set.Heights, set.NewNormals, set.Beyond); break;
                case CopyEarlierNavmeshGeometry copy: navmeshes.CopyEarlierGeometry(copy.Record); break;
                case CopyEarlierNavmesh copy: navmeshes.CopyEarlier(copy.Record); break;
                case FlattenAndDetachNavmesh flatten: navmeshes.FlattenAndDetach(flatten.Record, flatten.Z); break;
                case WriteNavmeshMapEntries entries: maps.WriteEntries(entries.Record, entries.Entries); break;
                default: placed.Write(order); break;
            }
        }
        return [.. overrides.Added.Select(key => key.KeyAsWritten())];
    }

    private static RecordHandles HandlesOf(IWritableRecords records) =>
        records as RecordHandles ?? throw new ArgumentException(ForeignRecordsMessage, nameof(records));
}
