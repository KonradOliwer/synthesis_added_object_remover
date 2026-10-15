using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>
/// Every visible placed object that is not a target object, replaced other-mod objects included. Lists ids in ascending
/// order and replaces the contents of a list the caller owns; the caller passes its own worker's scratch buffers.
/// </summary>
public interface IVisibleObjectsOfAnyPlugin
{
    /// <summary>Replaces <paramref name="into"/> with the visible objects whose world bounds overlap <paramref name="area"/>.</summary>
    void Overlapping(RecordKey space, Box area, ObjectQueryScratch scratch, List<OtherId> into);

    /// <summary>Replaces <paramref name="into"/> with the visible objects whose oriented box comes within <paramref name="distance"/> of <paramref name="box"/>.</summary>
    void Touching(RecordKey space, OrientedBox box, float distance, ObjectQueryScratch scratch, List<OtherId> into);

    /// <summary>Whether the mesh of some visible object surrounds <paramref name="point"/>.</summary>
    bool Contains(RecordKey space, Vector3 point, ObjectQueryScratch scratch);

    OtherObject Get(OtherId id);
}
