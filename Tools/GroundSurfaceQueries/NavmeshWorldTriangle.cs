namespace AddedObjectRemover;

/// <summary>A triangle of a navmesh, in world space, with the navmesh it belongs to.</summary>
public readonly record struct NavmeshWorldTriangle(RecordKey Navmesh, MeshTriangle Triangle);
