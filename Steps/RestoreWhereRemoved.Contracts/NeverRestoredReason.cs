namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>Why a change never goes back, whatever its area.</summary>
public enum NeverRestoredReason
{
    /// <summary>A plugin after the patch wins the record, so the patch's override would not show.</summary>
    WinnerLoadsAfterPatch,

    /// <summary>A record the cleaned mod creates links to it, and the cleaned mod's quests and scripts still run.</summary>
    LinkedFromCleanedMod,

    /// <summary>A plugin deletes a cell it would be written into, and nothing is written into a deleted cell.</summary>
    InDeletedCell,
}
