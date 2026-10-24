namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>The parts of the step for other mods' objects that record their unexpected errors; none has a subject.</summary>
public static class OtherModsObjectsParts
{
    private const string Owner = "RemoveObjectsRestingOnRemovedOnes";

    /// <summary>Finding the target objects other mods' objects rest on, to keep them.</summary>
    public static readonly PartKey HeldUpTargets = new(Owner, nameof(HeldUpTargets));

    /// <summary>Removing or pulling down the other mods' objects left without support.</summary>
    public static readonly PartKey LeftWithoutSupport = new(Owner, nameof(LeftWithoutSupport));
}
