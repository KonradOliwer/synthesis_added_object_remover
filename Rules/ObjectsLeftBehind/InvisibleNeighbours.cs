using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>
/// The neighbours measured around an invisible target: the visible targets within the look-around distance, or within its reach when
/// that is shorter.
/// </summary>
/// <param name="surroundings">
/// The visible target objects where the rounds measure them, with the look-around distance; null while the step is switched off,
/// which measures no neighbours.
/// </param>
internal sealed class InvisibleNeighbours(IBaseObjectShapes shapes, IInvisibleObjectReach reach, (IVisibleTargetObjects Index, float LookAround)? surroundings) : ILeftBehindNeighbours
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
