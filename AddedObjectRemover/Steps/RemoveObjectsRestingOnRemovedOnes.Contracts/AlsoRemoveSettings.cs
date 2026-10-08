namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <param name="TouchGap">Largest gap, in game units, between two surfaces that still counts as touching.</param>
/// <param name="SupportLostFraction">Fraction (0-1) of an object's support that must come from removed objects for ObjectsSupportedByIt to remove it.</param>
public sealed record AlsoRemoveSettings(FollowUpRemovalMode Mode, float TouchGap, float SupportLostFraction);
