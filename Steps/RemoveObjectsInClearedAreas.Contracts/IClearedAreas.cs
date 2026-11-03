using System.Numerics;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

/// <summary>How much of the cleaned mod's drawn objects still stand in the area around a point, on one set of decisions. Thread-safe.</summary>
public interface IClearedAreas
{
    /// <summary>The share standing in the area around <paramref name="point"/>; in an interior, in the whole interior.</summary>
    AreaShare Judge(RecordKey space, Vector3 point);

    /// <summary>The areas added up under the verdict they give together.</summary>
    AreaShare Sum(IReadOnlyCollection<AreaShare> areas, AreaVerdict verdict);
}
