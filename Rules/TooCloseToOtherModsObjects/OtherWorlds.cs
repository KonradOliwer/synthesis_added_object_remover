using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects;

/// <summary>What the too-close checks in worlds other than the run's read.</summary>
/// <param name="Caches">The run's caches: the shapes and the triangles, and the index of the run's target objects.</param>
/// <param name="WorldCaches">The object caches of the worlds asked about.</param>
/// <param name="Causes">The objects that can cause removals in those worlds, selected among <paramref name="WorldCaches"/>.</param>
internal sealed record OtherWorlds(IRunCaches Caches, IObjectCachesOfWorlds WorldCaches, ICausesOfWorlds Causes);
