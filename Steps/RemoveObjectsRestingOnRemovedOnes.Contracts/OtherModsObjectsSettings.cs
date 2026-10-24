namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <param name="TouchGap">Largest gap, in game units, between two surfaces that still counts as touching.</param>
/// <param name="SupportLostFraction">Fraction (0-1) of an object's support that must be gone for it to count as left without support.</param>
public sealed record OtherModsObjectsSettings(OtherModsObjectsHandling Handling, float TouchGap, float SupportLostFraction);
