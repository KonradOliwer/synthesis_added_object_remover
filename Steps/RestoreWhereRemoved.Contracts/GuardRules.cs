using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

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
