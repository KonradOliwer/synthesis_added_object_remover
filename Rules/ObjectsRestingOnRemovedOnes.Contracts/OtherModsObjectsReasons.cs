using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>The other-mods reason: the object touched another mod's object left without support.</summary>
/// <param name="Touched">Another mod's object left without support, removed or pulled down, that the object touched where it stood.</param>
public sealed record TouchingAnotherModsObject(OtherId Touched) : RemovalReason;

/// <summary>The other-mods reason: the object lost too much of its support, with another mod's object among what it lost.</summary>
/// <param name="LeftItsPlaceShare">Fraction of the object's support held by target objects that left their place, removed or restored, and other mods' objects taken away.</param>
/// <param name="RestoredShare">The part of <paramref name="LeftItsPlaceShare"/> held by restored target objects.</param>
/// <param name="MainSupporter">The other mod's object left without support, removed or pulled down, holding the largest share of its support.</param>
public sealed record LostSupportWithAnotherModsObject(float LeftItsPlaceShare, float RestoredShare, OtherId MainSupporter) : RemovalReason;
