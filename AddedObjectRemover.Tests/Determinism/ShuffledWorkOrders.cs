namespace AddedObjectRemover.Tests.Determinism;

/// <summary>Work orders that differ from the normal one but stay valid permutations of the targets.</summary>
internal static class ShuffledWorkOrders
{
    public static WorkOrder Reversed(WorkOrder order) => new([.. order.TargetsBySpaceAndCell.Reverse()]);

    public static WorkOrder Shuffled(WorkOrder order, int seed)
    {
        var random = new Random(seed);
        return new WorkOrder([.. order.TargetsBySpaceAndCell.OrderBy(_ => random.Next())]);
    }

    public static IEnumerable<WorkOrder> Variants(WorkOrder order) => [Reversed(order), Shuffled(order, 1), Shuffled(order, 2)];
}
