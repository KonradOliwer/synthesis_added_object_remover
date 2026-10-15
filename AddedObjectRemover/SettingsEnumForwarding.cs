using System.Runtime.CompilerServices;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

// The Synthesis settings screen looks up each enum setting's type only in this assembly (Assembly.GetType) and shows no
// control when it is missing. The enums live in the contracts assemblies, so each one is forwarded from here.
[assembly: TypeForwardedTo(typeof(TerrainCheckedObjects))]
[assembly: TypeForwardedTo(typeof(TerrainDistanceMode))]
[assembly: TypeForwardedTo(typeof(ZoneShape))]
[assembly: TypeForwardedTo(typeof(NpcHandling))]
[assembly: TypeForwardedTo(typeof(FollowUpRemovalMode))]
[assembly: TypeForwardedTo(typeof(ProtectedInvisibleObjectsPreset))]
[assembly: TypeForwardedTo(typeof(InvisibleObjectKind))]
