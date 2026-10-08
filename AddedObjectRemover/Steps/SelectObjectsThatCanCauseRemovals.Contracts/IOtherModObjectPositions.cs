using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

/// <summary>
/// The visible other-mod objects by where they are placed: only for matching replaced objects. Lists ids in
/// ascending order and replaces the contents of a list the caller owns.
/// </summary>
public interface IOtherModObjectPositions
{
    /// <summary>Replaces <paramref name="into"/> with the visible other-mod objects placed within <paramref name="radius"/> of <paramref name="point"/>.</summary>
    void Within(RecordKey space, Vector3 point, float radius, List<OtherId> into);

    OtherObject Get(OtherId id);
}
