using System.Numerics;

namespace AddedObjectRemover;

/// <summary>A placement as a placed record stores it: a position, an Euler rotation in radians and a scale.</summary>
public readonly record struct EulerPlacement(Vector3 Position, Vector3 Rotation, float Scale)
{
    public PlacedTransform Transform => new(Position, Mat3.FromEuler(Rotation), Scale);
}
