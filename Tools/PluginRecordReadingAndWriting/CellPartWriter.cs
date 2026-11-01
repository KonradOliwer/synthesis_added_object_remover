using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>Puts parts of cell records back to the version just below the target plugin's, in the patch's cell overrides. Not thread-safe.</summary>
internal sealed class CellPartWriter(CellOverrides cells, RecordHandles handles)
{
    /// <summary>
    /// The patch's cell takes, in each of <paramref name="parts"/>, the values of the version just below the target plugin's, through
    /// the part table the cell was compared with; every other part keeps the value of the cell the game shows now.
    /// </summary>
    public void CopyEarlierParts(RecordKey cell, ImmutableArray<CellPartName> parts)
    {
        var versions = handles.CellOf(cell);
        var table = handles.CellParts.For(versions.Versions);
        var target = cells.GetOrAdd(cell.ToFormKey());
        foreach (var name in parts)
        {
            var key = new CellPartKey(name.Part, name.Region?.ToFormKey());
            var (_, part) = table.Single(entry => entry.Key == key);
            if (!RawByteFormat.AllowsCopy(part, versions.Earlier, target))
                throw new InvalidOperationException($"Part {name.Part} of cell {cell} holds bytes of another record format and cannot go back.");
            part.CopyInto(target, versions.Earlier);
        }
    }
}
