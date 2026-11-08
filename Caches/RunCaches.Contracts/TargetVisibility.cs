using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

public static class TargetVisibility
{
    /// <summary>How the target looks in the game: visible, or which kind of invisible object it is.</summary>
    public static ObjectVisibility VisibilityOf(IBaseObjectShapes shapes, TargetObject target) =>
        shapes.VisibilityOf(target.Base, target.IsPrimitive, target.HasMapMarker);

    /// <summary>How the object of any plugin looks in the game: visible, or which kind of invisible object it is.</summary>
    public static ObjectVisibility VisibilityOf(IBaseObjectShapes shapes, OtherObject other) =>
        shapes.VisibilityOf(other.Base, other.IsPrimitive, other.HasMapMarker);
}
