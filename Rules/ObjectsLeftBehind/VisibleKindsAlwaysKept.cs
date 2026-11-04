using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>A shown object of a kind the settings never remove when left behind, by the kind of its base.</summary>
internal sealed class VisibleKindsAlwaysKept(IBaseObjectShapes shapes, IBaseFacts bases, IReadOnlySet<VisibleObjectKind> neverRemove) : IKindsAlwaysKept
{
    public AlwaysKeptKind? KindKeeping(TargetObject target) =>
        shapes.VisibilityOf(target).IsShown && VisibleObjectKinds.Of(target, bases) is { } kind && neverRemove.Contains(kind) ? new AlwaysKeptKind.Visible(kind) : null;
}
