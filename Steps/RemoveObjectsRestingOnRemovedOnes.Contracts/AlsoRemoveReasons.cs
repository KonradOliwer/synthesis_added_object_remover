using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>The also-remove step's reason: the object touched a target object that left its place.</summary>
public sealed record TouchingTargetThatLeft(TargetId Touched) : RemovalReason;

/// <summary>The also-remove step's reason: the object lost too much of its support.</summary>
/// <param name="LeftItsPlaceShare">Fraction of the object's support held by objects that left their place, removed or restored.</param>
/// <param name="RestoredShare">The part of <paramref name="LeftItsPlaceShare"/> held by restored objects.</param>
/// <param name="MainSupporter">The target that left its place and holds the largest share of its support.</param>
public sealed record LostSupport(float LeftItsPlaceShare, float RestoredShare, TargetId MainSupporter) : RemovalReason;
