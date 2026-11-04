using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>The neighbours the visible step measures around a shown target: the visible targets within its look-around distance.</summary>
/// <param name="surroundings">
/// The visible target objects where the rounds measure them, with the step's look-around distance; null while the step is switched
/// off, which measures no neighbours.
/// </param>
internal sealed class VisibleNeighbours(IBaseObjectShapes shapes, (IVisibleTargetObjects Index, float LookAround)? surroundings) : ILeftBehindNeighbours
{
    public void Around(TargetObject target, List<TargetId> into)
    {
        into.Clear();
        if (surroundings is not var (index, lookAround) || !shapes.VisibilityOf(target).IsShown) return;

        into.AddRange(Measure(target, index, lookAround, []).Select(neighbour => neighbour.Target));
    }

    /// <summary>The visible targets whose box lies within <paramref name="lookAround"/> of the target's position, the target left out, ascending.</summary>
    /// <param name="scratch">The calling worker's buffer.</param>
    public static VisibleTargetNear[] Measure(TargetObject target, IVisibleTargetObjects index, float lookAround, List<VisibleTargetNear> scratch)
    {
        index.Around(target.SpaceKey, target.Transform.Position, lookAround, scratch);
        return [.. scratch.Where(neighbour => neighbour.Target != target.Id)];
    }
}
