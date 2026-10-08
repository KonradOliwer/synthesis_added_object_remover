using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>A winning navmesh and the exterior cell holding it.</summary>
/// <param name="Grid">Null for a navmesh of an interior cell or of a worldspace's persistent cell.</param>
internal readonly record struct CellNavmesh((int X, int Y)? Grid, INavigationMeshDataGetter Data);
