using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <param name="Restored">The target object as its earlier version, where it goes back to.</param>
/// <param name="StartedBy">The keys of the candidates whose going back restores it, in key order.</param>
internal sealed record RestoredByFollowUp(TargetObject Restored, ImmutableArray<RecordKey> StartedBy);
