using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>Target objects that a step judges with one settings record.</summary>
public sealed record ObjectsJudgedWith<TSettings>(ImmutableArray<TargetId> Objects, TSettings Settings)
    where TSettings : notnull
{
    /// <returns>
    /// One group for each distinct settings, in the order their settings first appear; each holds its objects in target order.
    /// A step runs once for each of them.
    /// </returns>
    /// <remarks>Settings are compared by value, so a settings record used here holds only simple value fields.</remarks>
    public static ImmutableArray<ObjectsJudgedWith<TSettings>> MergeEqualSettings(IEnumerable<ObjectsJudgedWith<TSettings>> groups) =>
    [
        .. KeyedGroups.MergeGroupsWithEqualKeys(groups, group => group.Objects, group => group.Settings, EqualityComparer<TSettings>.Default)
            .Select(merged => new ObjectsJudgedWith<TSettings>(merged.Value, merged.Key)),
    ];
}
