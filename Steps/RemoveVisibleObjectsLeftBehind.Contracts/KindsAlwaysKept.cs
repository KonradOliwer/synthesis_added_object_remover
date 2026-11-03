using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>A kind of object a left-behind step's settings always keep.</summary>
public abstract record AlwaysKeptKind
{
    /// <summary>Listed by the step for visible objects left behind.</summary>
    public sealed record Visible(VisibleObjectKind Kind) : AlwaysKeptKind;

    /// <summary>Listed by the step for invisible objects left behind.</summary>
    public sealed record Invisible(InvisibleObjectKind Kind) : AlwaysKeptKind;
}

/// <summary>The rule of a left-behind step that keeps an object of a kind its settings list, whatever its surroundings.</summary>
public interface IKindsAlwaysKept
{
    /// <returns>The kind that keeps the object; null when the step judges no object of its kind or keeps none of it. The kinds are the ones the settings list, also while the step is switched off.</returns>
    AlwaysKeptKind? KindKeeping(TargetObject target);
}
