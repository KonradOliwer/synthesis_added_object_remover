using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>The test that finds the other mods' objects left without support, asked about objects that move away from where they stand now.</summary>
public interface IHoldersOfObjectsThatStay
{
    /// <summary>The largest gap the test counts as touching, which bounds how far from an object that moves away it looks.</summary>
    float TouchGap { get; }

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
    /// <param name="notAskedAbout">The objects that stay whose support is not asked about; they still hold up.</param>
    /// <returns>Each that moves away and holds up an object that stays, with the first such object in id order.</returns>
    IReadOnlyDictionary<TargetId, OtherObject> Find(Func<TargetId, bool> movesAway, IReadOnlySet<OtherId> copiesOfMoving, Func<OtherId, bool> notAskedAbout);
}
