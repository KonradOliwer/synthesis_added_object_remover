using System.Numerics;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>A kept marker moved out of the other mod's object it sat inside.</summary>
public readonly record struct MarkerMove(TargetId Target, Vector3 From, Vector3 To);
