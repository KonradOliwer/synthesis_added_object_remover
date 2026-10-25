using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>The also-remove step's rule asked, before any removal, which target objects other target objects rest on.</summary>
public interface IRestingRule
{
    /// <summary>
    /// Those of <paramref name="objects"/> that another target object rests on from above and would lose its support without,
    /// each object where it stands; none when the step removes nothing else.
    /// </summary>
    /// <returns>In target order.</returns>
    ImmutableArray<TargetId> FindObjectsOthersRestOn(IReadOnlyList<TargetId> objects);
}
