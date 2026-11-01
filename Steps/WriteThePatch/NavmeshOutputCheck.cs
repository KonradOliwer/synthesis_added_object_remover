namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>
/// Checks the navmeshes the patch wrote, and every navmesh linked to them, over the final world. The groups that go back passed the
/// same check on the same orders before, so a fault here is a bug of the writers.
/// </summary>
/// <remarks>
/// The check does not compare a navmesh's Preferred triangles with the preferred paths: the plan's N8 is the only guard of those.
/// </remarks>
internal static class NavmeshOutputCheck
{
    /// <param name="doorsLeftRemoved">The placed records the final world leaves removed although the patch writes no removal of them.</param>
    /// <param name="readFinalWorld">Read only when an order wrote a navmesh, a navmesh info map entry or a door's navmesh link.</param>
    public static void ThrowOnFault(IReadOnlyList<WriteOrder> orders, IReadOnlySet<RecordKey> doorsLeftRemoved, Func<INavmeshWorldView> readFinalWorld)
    {
        if (NavmeshWriteConsistency.FirstFault(orders, doorsLeftRemoved, readFinalWorld) is { } fault)
            throw new InvalidOperationException($"The patch's navmeshes break the navmesh consistency check: {fault}.");
    }
}
