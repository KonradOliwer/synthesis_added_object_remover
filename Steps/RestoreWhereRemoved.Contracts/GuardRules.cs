using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

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

/// <summary>The navmesh consistency check over the world that the patch's writes of a trial alone would leave.</summary>
public interface INavmeshCheckIfWritten
{
    /// <returns>The first fault; null when none.</returns>
    NavmeshFault? FindFaultIfWritten(NavmeshWriteTrial trial);
}

/// <summary>What a trial of the navmesh check has the patch write, in this order.</summary>
/// <param name="RemovedDoors">The doors the run removes, which the patch writes as removed.</param>
/// <param name="RestoredParts">The placed records whose parts take their values before the cleaned mod.</param>
/// <param name="Group">The navmesh group the trial puts back; null when it puts back none.</param>
public sealed record NavmeshWriteTrial(ImmutableArray<RecordKey> RemovedDoors, ImmutableArray<PlacedPartsRestored> RestoredParts, NavmeshGroupWrite? Group);

/// <summary>A placed record whose parts take their values before the cleaned mod.</summary>
public readonly record struct PlacedPartsRestored(RecordKey Record, PlacedParts Parts);
