using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;

/// <summary>The too-close rule that checks an earlier version standing at its old place.</summary>
public interface IOldPlaceTooCloseRule
{
    /// <summary>
    /// The rule that checks objects standing in <paramref name="oldPlaceWorld"/>, with the objects that exist only because of the
    /// cleaned mod as its causes, except <paramref name="leftOut"/>: the cleaned mod's own version of the object checked.
    /// </summary>
    IPlacementRule Create(CollectedObjects oldPlaceWorld, OtherId leftOut);
}
