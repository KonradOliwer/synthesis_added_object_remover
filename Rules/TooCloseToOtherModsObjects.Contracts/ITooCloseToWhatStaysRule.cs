using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

/// <summary>The too-close rule a placed record going back is checked by, among what stays.</summary>
public interface ITooCloseToWhatStaysRule
{
    /// <summary>
    /// The rule over <paramref name="world"/>, a world of what stays whose candidates are its target objects, with its other objects
    /// as causes except <paramref name="leftOut"/>.
    /// </summary>
    IPlacementRule Create(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings);
}
