using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>A cell record with parts the cleaned mod is the last to change.</summary>
/// <param name="Undone">Those parts, in the order of the cell's parts.</param>
internal sealed record CellCandidate(CellChangedByCleanedMod Change, ImmutableArray<PartChange> Undone);

/// <summary>The records of other plugins with parts the cleaned mod is the last to change, which the area rule may put back.</summary>
/// <param name="final">The decisions with every round and every move.</param>
/// <param name="otherModsObjects">What became of other mods' objects left without support.</param>
internal sealed class RestoreCandidates(CollectedObjects collected, IRemovalDecisions final, OtherModsObjectsResult otherModsObjects)
{
    /// <summary>
    /// The placed records with an undone part, in key order whatever order they come in, each with the parts its restore writes
    /// (<see cref="DoorLinks.WithLinkGoingBack"/>). A target counts only while the removal steps left it undecided where it stands and
    /// it is not among the objects to keep; one they decided, whose marker they moved, or that is kept, stays as it is. An object the
    /// other mods' objects step took away stays as that step left it.
    /// </summary>
    public ImmutableArray<RecordChangedByCleanedMod> PlacedRecords()
    {
        var takenAway = otherModsObjects.Objects.Where(outcome => outcome.IsTakenAway).Select(outcome => outcome.Object.Key).ToHashSet();
        var moved = final.Moves.Select(move => move.Target).ToHashSet();
        return
        [
            .. collected.ChangedRecords
                .Where(change => change.Earlier.Undo != PlacedParts.None && !takenAway.Contains(change.Key) && IsUndecidedAndNotKept(change.Target, moved))
                .OrderBy(change => change.Key, RecordKeyOrder.Comparer)
                .Select(DoorLinks.WithLinkGoingBack),
        ];
    }

    /// <summary>The cell records with an undone part, in key order whatever order they come in.</summary>
    public ImmutableArray<CellCandidate> Cells() =>
    [
        .. collected.ChangedCells
            .Select(change => new CellCandidate(change, [.. change.Parts.Where(part => part.Outcome is PartOutcome.Undo)]))
            .Where(cell => !cell.Undone.IsEmpty)
            .OrderBy(cell => cell.Change.Cell, RecordKeyOrder.Comparer),
    ];

    /// <summary>The LAND records with an undone part, in (worldspace, x, y) order of their cells whatever order they come in.</summary>
    public ImmutableArray<LandChangedByCleanedMod> Lands() =>
    [
        .. collected.ChangedLands
            .Where(change => !change.UndoPoints.IsEmpty || !change.UndoColours.IsEmpty || !change.UndoQuarters.IsEmpty || change.UndoRest)
            .OrderBy(change => change.Cell.WorldspaceKey, RecordKeyOrder.Comparer)
            .ThenBy(change => change.Cell.X)
            .ThenBy(change => change.Cell.Y),
    ];

    /// <param name="target">Null for a record that is no target.</param>
    /// <remarks>A kept target is content that stays, like a decided one, and what another plugin rests on it would be left floating.</remarks>
    private bool IsUndecidedAndNotKept(TargetId? target, IReadOnlySet<TargetId> moved) =>
        target is not { } id || (!final.IsDecided(id) && !final.IsProtected(id) && !moved.Contains(id));
}
