using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <param name="objectsOfAnyPlugin">Asked each time the rounds are created.</param>
internal sealed class AlsoRemoveRoundsAfter(
    AlsoRemoveSettings settings,
    IBaseObjectShapes shapes,
    ITriangleMeshes triangles,
    ISurfaceSamples surfaces,
    ITerrainHeights terrain,
    Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin,
    Execution execution) : IAlsoRemoveRoundsAfter
{
    public IRemovalRounds<RoundDetails> After(IRemovalDecisions decided, ImmutableArray<TargetObject> targets) =>
        RestingObjects.CreateRoundsAfter(decided, targets, objectsOfAnyPlugin(), settings, shapes, triangles, surfaces, terrain, execution);
}
