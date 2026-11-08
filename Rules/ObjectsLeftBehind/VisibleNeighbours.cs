using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>The neighbours measured around a shown target: the visible targets within the look-around distance.</summary>
/// <param name="surroundings">
/// The visible target objects where the rounds measure them, with the look-around distance; null while the step is switched off,
/// which measures no neighbours.
/// </param>
internal sealed class VisibleNeighbours(IBaseObjectShapes shapes, (IVisibleTargetObjects Index, float LookAround)? surroundings) : ILeftBehindNeighbours
{
    public void Around(TargetObject target, List<TargetId> into)
    {
        into.Clear();
        if (surroundings is not var (index, lookAround) || !TargetVisibility.VisibilityOf(shapes, target).IsShown) return;

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
