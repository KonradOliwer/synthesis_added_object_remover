namespace AddedObjectRemover;

/// <summary>The names a cell's Location chain gives, each the display name, else the editor id.</summary>
/// <param name="Hold">
/// The Location directly below the top of the chain, the top being the worldspace's own top Location when the chain reaches it,
/// else the last Location of the chain; the top itself when nothing is below it.
/// </param>
/// <param name="Place">The cell's own Location.</param>
public sealed record LocationNames(string Hold, string Place);

/// <summary>What a winning cell record says about where it is.</summary>
/// <param name="Space">The worldspace key, or the interior cell's own key.</param>
/// <param name="Grid">Null for an interior and for a worldspace's persistent cell.</param>
/// <param name="Name">The display name, else the editor id; null when it has neither.</param>
/// <param name="Location">Null when the cell has no Location, or it cannot be found or has no name.</param>
public sealed record CellAreaFact(RecordKey Space, CellGridPoint? Grid, string? Name, LocationNames? Location);
