namespace AddedObjectRemover;

internal static class Leftovers
{
    /// <returns>The evaluations without the ledger's verdicts joined; only the removals already in the input's ledger count as cleared.</returns>
    public static LeftoverResult Evaluate(LeftoverInput input, LeftoverOptions options)
    {
        var selector = new LeftoverInvisibleObjectSelector(
            input.Targets,
            input.Looks,
            input.Surroundings,
            input.Hosts,
            new InvisibleObjectReach(input.Bases),
            options,
            input.Order);
        return selector.SelectRemovals(RemovedIndexes(input.Ledger), input.Exec);
    }

    internal static HashSet<int> RemovedIndexes(Ledger ledger) =>
        ledger.All().Where(entry => entry.Verdict is Verdict.Removed).Select(entry => entry.Target.Index).ToHashSet();
}
