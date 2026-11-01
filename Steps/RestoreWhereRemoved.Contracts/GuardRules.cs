using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>The too-close rule a placed record going back is checked by, among what stays.</summary>
public interface ITooCloseToWhatStaysRule
{
    /// <summary>
    /// The rule over <paramref name="world"/>, a world of what stays whose candidates are its target objects, with its other objects
    /// as causes except <paramref name="leftOut"/>.
    /// </summary>
    IPlacementRule Create(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings);
}

/// <summary>The test that finds the other mods' objects left without support, asked about objects that move away from where they stand now.</summary>
public interface IHoldersOfObjectsThatStay
{
    /// <summary>The search over <paramref name="world"/>, a world of what stays, among <paramref name="standingNow"/>.</summary>
    /// <param name="world">Its visible objects stand as they stand once the run is done, with a copy of each of <paramref name="standingNow"/>.</param>
    /// <param name="standingNow">Where they stand now; the id of each is its position.</param>
    IHoldersSearch Over(CollectedObjects world, IReadOnlyList<TargetObject> standingNow);
}

/// <summary>One search of <see cref="IHoldersOfObjectsThatStay"/>, which may be asked several times.</summary>
public interface IHoldersSearch
{
    /// <param name="movesAway">Which of the objects standing now move away; the others stay, as their copies in the world.</param>
    /// <param name="copiesOfMoving">The copies of those that move away, which neither rest nor hold up.</param>
    /// <returns>Each that moves away and holds up an object that stays, with the first such object in id order.</returns>
    IReadOnlyDictionary<TargetId, OtherObject> Find(Func<TargetId, bool> movesAway, IReadOnlySet<OtherId> copiesOfMoving);
}

/// <summary>The navmesh consistency check over the world that write orders alone would leave.</summary>
public interface INavmeshCheckIfWritten
{
    /// <returns>The first fault; null when none.</returns>
    NavmeshFault? FindFaultIfWritten(IReadOnlyList<WriteOrder> orders);
}
