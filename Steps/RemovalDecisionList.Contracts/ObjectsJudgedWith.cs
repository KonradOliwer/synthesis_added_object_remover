using System.Collections.Immutable;
using System.Diagnostics;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>Target objects that a step judges with one settings record.</summary>
public sealed record ObjectsJudgedWith<TSettings>(ImmutableArray<TargetId> Objects, TSettings Settings);

public static class JudgedGroups
{
    /// <returns>
    /// One group for each distinct settings, in the order their settings first appear; each holds its objects in target order.
    /// A step runs once for each of them.
    /// </returns>
    /// <remarks>Settings are compared by value, so a settings record used here holds only simple value fields.</remarks>
    public static ImmutableArray<ObjectsJudgedWith<TSettings>> MergeEqualSettings<TSettings>(IEnumerable<ObjectsJudgedWith<TSettings>> groups)
        where TSettings : notnull
    {
        var all = groups.ToList();
        RequireEachObjectInOneGroup(all);
        return
        [
            .. all
                .GroupBy(group => group.Settings)
                .Select(equal => new ObjectsJudgedWith<TSettings>([.. equal.SelectMany(group => group.Objects).Order()], equal.Key)),
        ];
    }

    public static void RequireEachObjectInOneGroup<TSettings>(IEnumerable<ObjectsJudgedWith<TSettings>> groups)
    {
        HashSet<TargetId> judged = [];
        foreach (var id in groups.SelectMany(group => group.Objects))
        {
            if (!judged.Add(id)) throw new UnreachableException($"Target object {id.Index} is in more than one group.");
        }
    }
}
