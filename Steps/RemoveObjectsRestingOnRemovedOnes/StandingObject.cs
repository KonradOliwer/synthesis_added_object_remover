using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>An object with a mesh where it stands, as far as its support and its drop need, whichever plugin it belongs to.</summary>
/// <param name="Self">The object as a supporter, so it never holds itself up.</param>
internal readonly record struct StandingObject(RecordKey Space, PlacedTransform Transform, string MeshPath, OrientedBox Box, Supporter Self);
