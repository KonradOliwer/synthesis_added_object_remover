using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>Turns the patch plan into write orders and has the plugin writer run them.</summary>
internal static class PatchWriter
{
    public static WriteSummary Run(PatchPlan plan, ImmutableArray<TargetObject> targets, IPluginRecords plugin)
    {
        var orders = new List<WriteOrder>();
        var enableParentsReplaced = 0;
        foreach (var id in plan.Remove)
        {
            var target = targets[id.Index];
            var hasEnableParent = plugin.HasEnableParent(target.Key);
            orders.AddRange(RemovalWrite.OrdersFor(target, hasEnableParent));
            if (hasEnableParent) enableParentsReplaced++;
        }

        foreach (var move in plan.Move)
        {
            orders.Add(new SetPosition(targets[move.Target.Index].Key, move.To));
        }

        plugin.Write(orders);
        return new WriteSummary(plan.Remove.Length, plan.Move.Length, enableParentsReplaced);
    }
}
