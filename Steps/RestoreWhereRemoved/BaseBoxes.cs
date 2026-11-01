using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>The box a placed object's base fills where it stands.</summary>
internal static class BaseBoxes
{
    public static OrientedBox Of(IBaseObjectShapes shapes, BaseKey? model, PlacedTransform transform) => OrientedBox.FromLocal(shapes.Of(model).Box, transform);
}
