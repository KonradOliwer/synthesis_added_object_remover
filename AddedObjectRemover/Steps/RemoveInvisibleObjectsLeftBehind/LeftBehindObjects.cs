using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

internal static class LeftBehindObjects
{
    /// <returns>The evaluations without the decisions joined; only the removals already in the input's decisions count as cleared.</returns>
    public static LeftBehindResult Evaluate(LeftBehindInput input, LeftBehindOptions options)
    {
        var rule = new LeftBehindRule(
            input.Targets,
            input.Shapes,
            input.Surroundings,
            input.Hosts,
            new ReachRule(input.Bases),
            options,
            input.Order);
        return rule.SelectRemovals(RemovedIndexes(input.RemovalDecisions), input.Exec);
    }

    internal static HashSet<int> RemovedIndexes(IRemovalDecisions decisions) =>
        decisions.RemovedTargets().Select(target => target.Index).ToHashSet();
}
