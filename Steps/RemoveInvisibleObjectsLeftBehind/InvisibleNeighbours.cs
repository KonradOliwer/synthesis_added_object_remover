using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

/// <summary>
/// The neighbours the invisible step measures around an invisible target: the visible targets within its look-around distance, or
/// within its reach when that is shorter.
/// </summary>
/// <param name="surroundings">
/// The visible target objects where the rounds measure them, with the step's look-around distance; null while the step is switched
/// off, which measures no neighbours.
/// </param>
internal sealed class InvisibleNeighbours(IBaseObjectShapes shapes, ReachRule reach, (IVisibleTargetObjects Index, float LookAround)? surroundings) : ILeftBehindNeighbours
{
    public void Around(TargetObject target, List<TargetId> into)
    {
        into.Clear();
        if (surroundings is not var (index, lookAround) || shapes.VisibilityOf(target).Kind is null) return;

        List<VisibleTargetNear> near = [];
        index.Around(target.SpaceKey, target.Transform.Position, reach.RadiusAround(target, lookAround), near);
        into.AddRange(near.Select(neighbour => neighbour.Target));
    }
}
