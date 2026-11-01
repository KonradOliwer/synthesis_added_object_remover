using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>What the decisions do with a target object that a round removes.</summary>
public abstract record WhenRemoved
{
    /// <summary>The object is disabled in the patch, so it stands nowhere.</summary>
    public sealed record Disable : WhenRemoved;

    /// <summary>
    /// The object's mode restores it, but the part that shows the cleaned mod's change (the position of a moved object, the base of a
    /// replaced one) does not go back, or the restore would write it into a cell the final world deletes, so it is disabled in the
    /// patch instead.
    /// </summary>
    /// <param name="ChangedLaterBy">The plugin that changed that part again after the cleaned mod; null when no plugin did and the part stays for another reason.</param>
    /// <param name="CellDeleted">The deleted cell the restore would write into, when the part goes back; null otherwise.</param>
    public sealed record DisableInsteadOfRestore(PluginName? ChangedLaterBy, DeletedCell? CellDeleted) : WhenRemoved;

    /// <summary>The object goes back to the earlier version of its record.</summary>
    /// <param name="To">The version it goes back to.</param>
    public sealed record Restore(EarlierVersion To) : WhenRemoved;
}
