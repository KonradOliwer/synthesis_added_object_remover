using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

/// <summary>The other-mod objects that can clash with a target: visible and not replaced; placed NPCs only when they count like objects.</summary>
public interface IObjectsThatCanCauseRemovals
{
    /// <summary>Replaces <paramref name="into"/> with the objects whose oriented box overlaps <paramref name="box"/>.</summary>
    /// <returns>How many objects' world bounds overlap the world bounds of <paramref name="box"/>, able to cause removals or not, before the oriented boxes are tested: a work counter.</returns>
    int Overlapping(RecordKey space, OrientedBox box, ObjectQueryScratch scratch, List<OtherId> into);

    /// <summary>The lowest object whose world bounds overlap <paramref name="area"/> and whose bounds centre passes <paramref name="centreIsInside"/>, or null.</summary>
    OtherId? FirstWithCentreInside(RecordKey space, Box area, Func<Vector3, bool> centreIsInside, ObjectQueryScratch scratch);

    /// <summary>The lowest object whose mesh surrounds <paramref name="point"/>, or null.</summary>
    OtherId? FirstCovering(RecordKey space, Vector3 point, ObjectQueryScratch scratch);

    OtherObject Get(OtherId id);

    /// <summary>The world-space centre of an object's bounds.</summary>
    Vector3 CentreOf(OtherId id);

    /// <summary>The objects of the space too large for the grid, which every query of the space tests.</summary>
    int LargeObjectCount(RecordKey space);
}
