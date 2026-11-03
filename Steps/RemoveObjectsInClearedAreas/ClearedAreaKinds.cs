using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas;

/// <summary>The one place that tells which kind of object the area method treats a target as.</summary>
internal static class ClearedAreaKinds
{
    /// <returns>Null for an object whose base could not be read, which the step neither judges nor counts.</returns>
    public static ClearedAreaObjectKind? Of(IBaseObjectShapes shapes, TargetObject target)
    {
        var visibility = shapes.VisibilityOf(target);
        if (visibility.Kind is not null) return ClearedAreaObjectKind.Invisible;
        if (!visibility.IsShown) return null;
        return visibility.EffectOnlyMesh ? ClearedAreaObjectKind.EffectOnly : ClearedAreaObjectKind.Drawn;
    }
}
