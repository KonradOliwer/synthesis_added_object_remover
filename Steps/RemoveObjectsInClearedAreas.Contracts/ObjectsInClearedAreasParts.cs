namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

/// <summary>The part of the step for objects in cleared areas that records its user-data failures; it has no subject.</summary>
public static class ObjectsInClearedAreasParts
{
    private const string Owner = "RemoveObjectsInClearedAreas";

    /// <summary>The step as a whole.</summary>
    public static readonly PartKey ObjectsInClearedAreas = new(Owner, nameof(ObjectsInClearedAreas));
}
