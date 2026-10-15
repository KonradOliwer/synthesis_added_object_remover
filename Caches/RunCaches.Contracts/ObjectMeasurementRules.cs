using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>How each object index measures a placed object; the select step supplies the rules.</summary>
/// <param name="OtherModObjects">The rule of the other mods' objects.</param>
/// <param name="ObjectsOfAnyPlugin">The rule of the objects of any plugin.</param>
public sealed record ObjectMeasurementRules(
    Func<IBaseObjectShapes, OtherObject, ObjectMeasurement> OtherModObjects,
    Func<IBaseObjectShapes, OtherObject, ObjectMeasurement> ObjectsOfAnyPlugin);
