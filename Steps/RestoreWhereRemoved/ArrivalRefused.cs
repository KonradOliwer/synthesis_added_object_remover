using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>Why a candidate does not fit where it arrives.</summary>
/// <param name="Causes">
/// What it found there, the main cause first: each object or target whose support it lost, the most lost first, the LAND record under
/// it for ground, or the cell record whose water line it lies across; none that names a record is left out.
/// </param>
internal readonly record struct ArrivalRefused(ArrivalMisfit Misfit, ImmutableArray<RecordKey> Causes);
