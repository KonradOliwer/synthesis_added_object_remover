namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>Which of the two shares of an area, by size and by number, must be at most its limit for the area to count as cleared.</summary>
public enum ClearedAreaShares
{
    /// <summary>Both the share by size and the share by number.</summary>
    Both,

    /// <summary>Either share.</summary>
    Either,
}
