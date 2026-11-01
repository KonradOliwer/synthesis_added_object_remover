namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>What the cleaned mod's objects in an area are weighed by, to tell what share of them still stands.</summary>
public enum ClearedAreaMeasure
{
    /// <summary>Each object weighs the volume of its box.</summary>
    ObjectSize,

    /// <summary>Each object weighs 1.</summary>
    ObjectCount,
}
