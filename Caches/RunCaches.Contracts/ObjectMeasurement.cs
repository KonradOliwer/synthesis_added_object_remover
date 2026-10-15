using System.Numerics;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>What the object indexes keep of one placed object: whether it counts as visible and the world-space centre of its bounds.</summary>
/// <param name="Center">Meaningless for an invisible object.</param>
public readonly record struct ObjectMeasurement(bool IsVisible, Vector3 Center);
