using System.Runtime.CompilerServices;
using AddedObjectRemover;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;

// The Synthesis settings screen looks up each enum setting's type only in this assembly (Assembly.GetType) and shows no
// control when it is missing. The enums live in the contracts assemblies, so each one is forwarded from here.
[assembly: TypeForwardedTo(typeof(PatchMode))]
[assembly: TypeForwardedTo(typeof(TerrainCheckedObjects))]
[assembly: TypeForwardedTo(typeof(NpcHandling))]
[assembly: TypeForwardedTo(typeof(FollowUpRemovalMode))]
[assembly: TypeForwardedTo(typeof(OtherModsObjectsHandling))]
[assembly: TypeForwardedTo(typeof(RestoreMode))]
