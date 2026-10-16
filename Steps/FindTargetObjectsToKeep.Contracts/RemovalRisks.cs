using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>Target objects that may still be removed, but whose removal the log warns about.</summary>
/// <param name="UniqueActors">The placed NPCs whose base a quest alias fills as its Unique Actor, in target order.</param>
/// <param name="LargeReferences">The target objects in a worldspace's large-reference list, in target order.</param>
public sealed record RemovalRisks(ImmutableArray<UniqueActorRole> UniqueActors, ImmutableArray<TargetId> LargeReferences);

/// <param name="Alias">The link from the first quest found whose alias fills with the NPC's base as its Unique Actor.</param>
public sealed record UniqueActorRole(TargetId Target, LinkFact Alias);
