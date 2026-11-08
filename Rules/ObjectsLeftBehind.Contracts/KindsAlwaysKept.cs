using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <summary>The kinds each left-behind step's settings always keep, read also while that step is switched off.</summary>
public sealed record AllKindsAlwaysKept(IKindsAlwaysKept Visible, IKindsAlwaysKept Invisible)
{
    /// <summary>Both rules, as the steps that judge visible and invisible objects alike take them.</summary>
    public IReadOnlyList<IKindsAlwaysKept> Both => [Visible, Invisible];
}

/// <summary>The rule of a left-behind step that keeps an object of a kind its settings list, whatever its surroundings.</summary>
public interface IKindsAlwaysKept
{
    /// <returns>Whether the object is of a kind the settings list; false when the step judges no object of its kind. The kinds are the ones the settings list, also while the step is switched off.</returns>
    bool Keeps(TargetObject target);
}
