using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

/// <summary>An invisible object of a kind the step's settings never remove when left behind.</summary>
internal sealed class InvisibleKindsAlwaysKept(IBaseObjectShapes shapes, IReadOnlySet<InvisibleObjectKind> neverRemove) : IKindsAlwaysKept
{
    public AlwaysKeptKind? KindKeeping(TargetObject target) =>
        shapes.VisibilityOf(target).Kind is { } kind && neverRemove.Contains(kind) ? new AlwaysKeptKind.Invisible(kind) : null;
}
