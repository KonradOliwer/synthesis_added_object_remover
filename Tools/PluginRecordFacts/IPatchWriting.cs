namespace AddedObjectRemover;

/// <summary>The load-order records behind the placed records that were read.</summary>
public interface IWritableRecords;

/// <summary>Writes override records into the patch plugin. Not thread-safe.</summary>
public interface IPatchWriting
{
    /// <summary>Writes each order as an override of the record in its cell.</summary>
    /// <param name="records">The records of the reader that read the placed records the orders address.</param>
    void Write(IWritableRecords records, IReadOnlyList<WriteOrder> orders);

    /// <summary>The navmeshes, navmesh info map and doors of the final world: the patch's records on top of the winning records read.</summary>
    /// <param name="records">The records the orders were written from.</param>
    INavmeshWorldView ReadFinalNavmeshes(IWritableRecords records);

    /// <summary>
    /// Writes the orders into an empty trial plugin, never into the patch, and runs <see cref="NavmeshWriteConsistency.FirstFault"/>
    /// over the world it gives: the navmeshes, map and doors as these orders alone would leave them on top of the winning records read.
    /// Only the orders' own removals count as doors that do not stand.
    /// </summary>
    /// <param name="records">The records the orders address.</param>
    NavmeshFault? FindNavmeshFaultIfWritten(IWritableRecords records, IReadOnlyList<WriteOrder> orders);
}
