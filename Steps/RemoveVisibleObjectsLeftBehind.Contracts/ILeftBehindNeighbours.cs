using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>
/// The neighbours a left-behind step measures around an object it judges: the visible target objects of its surroundings, where the
/// rounds of the decisions it was built on measure them. Thread-safe.
/// </summary>
public interface ILeftBehindNeighbours
{
    /// <summary>
    /// Replaces <paramref name="into"/> with the visible targets the step measures around <paramref name="target"/>, ascending; with
    /// none when the step judges no object of its kind.
    /// </summary>
    /// <param name="target">Where the rounds measure it.</param>
    void Around(TargetObject target, List<TargetId> into);
}
