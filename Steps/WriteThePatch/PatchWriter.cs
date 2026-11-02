using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>
/// Turns the final removal decisions, what became of other mods' objects and what goes back where the cleaned mod was removed into
/// write orders and has the patch writing run them.
/// </summary>
public static class PatchWriter
{
    public sealed record Input(
        IRemovalDecisions Final,
        ImmutableArray<TargetObject> Targets,
        OtherModsObjectsResult OtherModsObjects,
        RestoreWhereRemovedResult WhereRemoved,
        IPluginRecords Plugin,
        IPatchWriting Writing);

    public static WriteSummary Write(Input input)
    {
        var (final, targets, otherModsObjects, whereRemoved) = (input.Final, input.Targets, input.OtherModsObjects, input.WhereRemoved);
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

        var restored = ListRestoresInWriteOrder(final);
        orders.AddRange(restored.SelectMany(id => RestoreOrdersFor(targets[id.Index], ((Decision.Restored)final.Of(id)!).EarlierVersion)));
        var whereRemovedOrders = WhereRemovedOrders(whereRemoved).ToList();
        orders.AddRange(whereRemovedOrders);

        var moves = final.Moves.Select(move => new SetPosition(targets[move.Target.Index].Key, move.To)).ToList();
        orders.AddRange(moves);

        var otherRemovals = otherModsObjects.Objects.Where(outcome => outcome.IsRemoved).ToList();
        orders.AddRange(otherRemovals.SelectMany(outcome => RemovalWrite.OrdersFor(outcome.Object)));
        var pulled = otherModsObjects.Objects.Where(outcome => outcome.Fate == OtherModsObjectFate.PulledDown).Select(outcome => new SetPosition(outcome.Object.Key, outcome.NewPosition)).ToList();
        orders.AddRange(pulled);

        var records = input.Plugin.ReadWritableRecords();
        input.Writing.Write(records, orders);
        NavmeshOutputCheck.ThrowOnFault(orders, whereRemoved.DoorsLeftRemoved.ToHashSet(), () => input.Writing.ReadFinalNavmeshes(records));
        return new WriteSummary(
            removed, restored, moves.Count, enableParentsReplaced, fitted.Count, otherRemovals.Count, pulled.Count, whereRemovedOrders.Select(order => order.Record).Distinct().Count());
    }

    /// <summary>The restore step's navmesh check, which has the patch writing try the write orders without writing them.</summary>
    public static INavmeshCheckIfWritten CreateNavmeshCheck(IPluginRecords plugin, IPatchWriting writing) => new NavmeshCheckIfWritten(plugin, writing);

    /// <summary>Round by round, each round's direct removals in target order, then the linked ones.</summary>
    internal static ImmutableArray<TargetId> ListRemovalsInWriteOrder(IRemovalDecisions final) =>
        [.. final.Rounds.SelectMany(round => final.RemovedIn(round))];

    /// <summary>Round by round, in the order of <see cref="ListRemovalsInWriteOrder"/>.</summary>
    private static ImmutableArray<TargetId> ListRestoresInWriteOrder(IRemovalDecisions final) =>
        [.. final.Rounds.SelectMany(round => final.RestoredIn(round))];

    /// <summary>Each fitted object's new placement, round by round, in target order.</summary>
    private static List<SetPlacement> ListFitsInWriteOrder(IRemovalDecisions final, ImmutableArray<TargetObject> targets) =>
    [
        .. final.Rounds
            .SelectMany(round => final.FittedIn(round))
            .Select(id => ToSetPlacement(targets[id.Index].Key, ((Decision.Fitted)final.Of(id)!).To)),
    ];

    private static IEnumerable<WriteOrder> RestoreOrdersFor(TargetObject target, EarlierVersion earlier) =>
        PlacedUndoWrite.OrdersFor(target.Key, target.Cell, target.IsPersistent, earlier);

    /// <summary>
    /// The placed records, then the cells, each in record key order, then the LAND records, in the order of their cells, then the
    /// navmesh groups, in their order, that go back where the cleaned mod was removed.
    /// </summary>
    private static IEnumerable<WriteOrder> WhereRemovedOrders(RestoreWhereRemovedResult whereRemoved) =>
        whereRemoved.Objects.Where(judged => judged.IsRestored)
            .SelectMany(judged => PlacedUndoWrite.OrdersFor(judged.Change.Key, judged.Change.Cell, judged.Change.IsPersistent, judged.Change.Earlier))
            .Concat(whereRemoved.Cells.Where(judged => judged.IsRestored)
                .Select(judged => new CopyEarlierCellParts(judged.Change.Cell, [.. judged.Undone.Select(part => part.Name)])))
            .Concat(whereRemoved.Lands.Where(judged => judged.IsRestored).SelectMany(LandOrdersFor))
            .Concat(whereRemoved.NavmeshGroups.Select(group => group.Write).OfType<NavmeshGroupWrite>().SelectMany(NavmeshUndoWrite.OrdersFor));

    /// <summary>The parts copied back first, then the heights, so the heights are written into the grids the rest brings back.</summary>
    private static IEnumerable<WriteOrder> LandOrdersFor(LandChangeJudged land)
    {
        if (!land.Colours.IsEmpty || !land.Textures.IsEmpty || land.Rest) yield return new CopyEarlierLandParts(land.Change.LandKey, land.Colours, land.Textures, land.Rest);
        if (land.FinalHeights is { } heights) yield return new SetLandHeights(land.Change.LandKey, heights, land.Heights, land.Beyond);
    }

    private static SetPlacement ToSetPlacement(RecordKey key, EulerPlacement placement) => new(key, placement.Position, placement.Rotation, placement.Scale);
}
