using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>An invisible object of a kind the settings never remove when left behind.</summary>
internal sealed class InvisibleKindsAlwaysKept(IBaseObjectShapes shapes, IReadOnlySet<InvisibleObjectKind> neverRemove) : IKindsAlwaysKept
{
    public bool Keeps(TargetObject target) =>
        shapes.VisibilityOf(target).Kind is { } kind && neverRemove.Contains(kind);
}
