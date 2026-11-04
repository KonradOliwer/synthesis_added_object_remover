using System.Collections.Immutable;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>The also-remove rule asked, before any removal, which target objects other target objects rest on.</summary>
public interface IRestingRule
{
    /// <summary>
    /// Those of <paramref name="objects"/> that another target object rests on from above and would lose its support without,
    /// each object where it stands; none when nothing else is removed.
    /// </summary>
    /// <returns>In target order.</returns>
    ImmutableArray<TargetId> FindObjectsOthersRestOn(IReadOnlyList<TargetId> objects);
}
