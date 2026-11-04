using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;

/// <summary>Whether the game shows another mod's placed record differently from its accepted values, which makes it a cause of removals.</summary>
public interface IOtherRecordChange
{
    /// <returns>Null when the game shows every object property as accepted.</returns>
    ObjectChange? Decide(PlacedRecordFact record, AcceptedWorld world);
}
