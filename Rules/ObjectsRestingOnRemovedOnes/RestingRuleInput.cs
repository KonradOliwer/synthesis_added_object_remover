using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <param name="Targets">Where they stand before any object is shrunk or turned.</param>
/// <param name="Drops">Asks the drop test about the same targets where they stand.</param>
/// <param name="Caches">Its shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked only when there is an object to check.</param>
internal sealed record RestingRuleInput(
    ImmutableArray<TargetObject> Targets, AlsoRemoveSettings Settings, ITargetObjectsDropTest Drops, Execution Execution, IRunCaches Caches);
