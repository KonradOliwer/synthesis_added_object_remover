using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>Turns the final removal decisions into write orders and has the plugin writer run them.</summary>
public static class PatchWriter
{
    public static WriteSummary Write(IRemovalDecisions final, ImmutableArray<TargetObject> targets, IPluginRecords plugin)
    {
        var removed = ListRemovalsInWriteOrder(final);
        var orders = new List<WriteOrder>();
        var enableParentsReplaced = 0;
        foreach (var id in removed)
        {
            var target = targets[id.Index];
            var hasEnableParent = plugin.HasEnableParent(target.Key);
            orders.AddRange(RemovalWrite.OrdersFor(target, hasEnableParent));
            if (hasEnableParent) enableParentsReplaced++;
        }

        foreach (var move in final.Moves)
        {
            orders.Add(new SetPosition(targets[move.Target.Index].Key, move.To));
        }

        plugin.Write(orders);
        return new WriteSummary(removed, final.Moves.Length, enableParentsReplaced);
    }

    /// <summary>Round by round, each round's direct removals in target order, then the linked ones.</summary>
    internal static ImmutableArray<TargetId> ListRemovalsInWriteOrder(IRemovalDecisions final) =>
        [.. final.Rounds.SelectMany(round => final.RemovedIn(round))];
}
