using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>The search of the removal zone shape in use, over given targets, or for one target at a time.</summary>
internal interface ITooCloseSearch
{
    /// <summary>The large other-mod objects of the spaces with visible targets that the search tests against every target of their space.</summary>
    int LargeOtherObjects { get; }

    /// <param name="judged">The targets to search, in target order.</param>
    TooCloseSearchResult FindTooCloseTargets(ImmutableArray<TargetId> judged, WorkOrder order, Execution execution);

    /// <summary>The checks of one target at placements tried one after another on one thread; their work is not counted.</summary>
    ITargetTries StartTries();
}

/// <summary>
/// The checks of one target, each given the target at the placement tried. The first is exactly the search's own check; a later
/// one hits or fails as the search's own check would, unless a mesh test stops unexpectedly, and may hit another object.
/// </summary>
internal interface ITargetTries : IDisposable
{
    TargetCheck Check(TargetObject placed);
}
