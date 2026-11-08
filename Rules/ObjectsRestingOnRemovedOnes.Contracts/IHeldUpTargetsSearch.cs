using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>The search for the target objects that other mods' objects rest on, which are kept for that reason.</summary>
public interface IHeldUpTargetsSearch
{
    /// <summary>The target objects other mods' objects rest on, each where it stands before any decision; a user-data failure stops only the search.</summary>
    HeldUpTargets Find();
}
