using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>Turns the final removal decisions and what became of other mods' objects into write orders and has the patch writing run them.</summary>
public static class PatchWriter
{
    public static WriteSummary Write(
        IRemovalDecisions final, ImmutableArray<TargetObject> targets, OtherModsObjectsResult otherModsObjects, IPluginRecords plugin, IPatchWriting writing)
    {
        var removed = ListRemovalsInWriteOrder(final);
        var orders = new List<WriteOrder>();
        var enableParentsReplaced = 0;
        foreach (var id in removed)
        {
            var target = targets[id.Index];
            orders.AddRange(RemovalWrite.OrdersFor(target));
            if (target.HasEnableParent) enableParentsReplaced++;
        }

        var fitted = ListFitsInWriteOrder(final, targets);
        orders.AddRange(fitted);

        var moves = final.Moves.Select(move => new SetPosition(targets[move.Target.Index].Key, move.To)).ToList();
        orders.AddRange(moves);

        var otherRemovals = otherModsObjects.Objects.Where(IsRemoved).ToList();
        orders.AddRange(otherRemovals.SelectMany(outcome => RemovalWrite.OrdersFor(outcome.Object)));
        var pulled = otherModsObjects.Objects.Where(outcome => outcome.Fate == OtherModsObjectFate.PulledDown).Select(outcome => new SetPosition(outcome.Object.Key, outcome.NewPosition)).ToList();
        orders.AddRange(pulled);

        writing.Write(plugin.ReadWritableRecords(), orders);
        return new WriteSummary(removed, moves.Count, enableParentsReplaced, fitted.Count, otherRemovals.Count, pulled.Count);
    }

    /// <summary>Round by round, each round's direct removals in target order, then the linked ones.</summary>
    internal static ImmutableArray<TargetId> ListRemovalsInWriteOrder(IRemovalDecisions final) =>
        [.. final.Rounds.SelectMany(round => final.RemovedIn(round))];

    /// <summary>Each fitted object's new placement, round by round, in target order.</summary>
    private static List<SetPlacement> ListFitsInWriteOrder(IRemovalDecisions final, ImmutableArray<TargetObject> targets) =>
    [
        .. final.Rounds
            .SelectMany(round => final.FittedIn(round))
            .Select(id => new SetPlacement(targets[id.Index].Key, ((Decision.Fitted)final.Of(id)!).To)),
    ];

    private static bool IsRemoved(OtherModsObjectOutcome outcome) => outcome.Fate is OtherModsObjectFate.Removed or OtherModsObjectFate.RemovedNothingBelow;
}
