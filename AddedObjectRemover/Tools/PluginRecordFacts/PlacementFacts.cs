using System.Numerics;

namespace AddedObjectRemover;

/// <param name="Position">Null when the record has no placement.</param>
/// <param name="EulerRotation">Null when the record has no placement.</param>
public readonly record struct PlacementFacts(bool InitiallyDisabled, bool HasEnableParent, Vector3? Position, Vector3? EulerRotation);
