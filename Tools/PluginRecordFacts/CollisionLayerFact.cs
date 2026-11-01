using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>A collision layer record (COLL) at its winning version.</summary>
/// <param name="Index">The layer number the references (XTRI) and the mesh files store.</param>
/// <param name="IsTrigger">The record's Trigger Volume or Sensor flag is set: things on the layer report contact and are not pushed back.</param>
/// <param name="IsNavmeshObstacle">The record's Navmesh Obstacle flag is set: the game keeps navmesh paths out of things on the layer.</param>
/// <param name="CollidesWith">The layer numbers of the records its "collides with" list names, in list order; links that resolve to no record are left out.</param>
public sealed record CollisionLayerFact(uint Index, bool IsTrigger, bool IsNavmeshObstacle, ImmutableArray<uint> CollidesWith);
