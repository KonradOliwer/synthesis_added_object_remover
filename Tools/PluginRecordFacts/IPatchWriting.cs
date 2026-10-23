namespace AddedObjectRemover;

/// <summary>The load-order records behind the placed records that were read.</summary>
public interface IWritableRecords;

/// <summary>Writes override records into the patch plugin. Not thread-safe.</summary>
public interface IPatchWriting
{
    /// <summary>Writes each order as an override of the record in its cell.</summary>
    /// <param name="records">The records of the reader that read the placed records the orders address.</param>
    void Write(IWritableRecords records, IReadOnlyList<WriteOrder> orders);
}
