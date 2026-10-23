using System.Numerics;

namespace AddedObjectRemover;

/// <param name="Parent">The record the enable parent names.</param>
/// <param name="Opposite">The record's enable state is set to the opposite of the parent's.</param>
public readonly record struct EnableParentFact(RecordKey Parent, bool Opposite);

/// <summary>The flags of one version of a record that say whether the game has the record at all; they are not judged here.</summary>
/// <param name="Deleted">This version is a deletion.</param>
/// <param name="EnableParent">Null when the record has none, or one whose reference is empty: that parent enables nothing.</param>
public readonly record struct PresenceFacts(bool Deleted, bool InitiallyDisabled, EnableParentFact? EnableParent);

/// <param name="Position">Null when the record has no placement.</param>
/// <param name="EulerRotation">Null when the record has no placement.</param>
public readonly record struct PlacementFacts(PresenceFacts Presence, Vector3? Position, Vector3? EulerRotation);
