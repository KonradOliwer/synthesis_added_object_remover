using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>The search of the removal zone shape in use, over every target or for one target alone.</summary>
internal interface ITooCloseSearch
{
    /// <summary>The large other-mod objects of the spaces with visible targets that the search tests against every target of their space.</summary>
    int LargeOtherObjects { get; }

    TooCloseSearchResult FindTooCloseTargets(WorkOrder order, Execution execution);

    /// <summary>One target alone, with a scratch of its own, so its work is not counted.</summary>
    TargetCheck CheckAlone(TargetObject target);
}
