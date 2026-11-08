using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <param name="Landed">
/// The object where it lands; its id as among the run's objects of any plugin, or after them for a candidate the game shows again.
/// </param>
/// <param name="StartedBy">The keys of the candidates whose going back pulls it down, in key order.</param>
/// <param name="ByGround">The object where it stands now, when ground going down started its pull too; null otherwise.</param>
internal sealed record PulledByFollowUp(OtherObject Landed, ImmutableArray<RecordKey> StartedBy, OtherObject? ByGround);
