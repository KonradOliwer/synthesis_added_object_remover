using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <param name="Caches">Its shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked each time the rounds are created.</param>
internal sealed record RoundsAfterInput(AlsoRemoveSettings Settings, Execution Execution, IRunCaches Caches);
