namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>
/// Whether a target object holds up another, by the drop test that finds the other mods' objects left without support, in the
/// world before any decision: every target object and every visible object of any plugin where it stands, and the terrain.
/// </summary>
public interface ITargetObjectsDropTest
{
    /// <param name="pairs">Each target object that may hold up another, with that other, a visible target object with a mesh.</param>
    /// <returns>Per pair, in the given order: whether the other rests on what is below it and no longer does without the first.</returns>
    bool[] FindHoldingUp(IReadOnlyList<(TargetId Holder, TargetId Resting)> pairs);
}
