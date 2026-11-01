using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// A navmesh of a target space that the cleaned mod overrides, deletes or adds, with what goes back to its version just below the
/// cleaned mod's and what a later plugin changed again.
/// </summary>
/// <param name="Cell">The cell record whose navmesh list holds the winning version.</param>
/// <param name="Geometry">The navmesh's data and raw navmesh bytes.</param>
/// <param name="Presence">The Deleted flag; for an added navmesh, its being there at all.</param>
/// <param name="MapEntry">Its entry in the navmesh info map; unchanged when the cleaned mod's map carries none.</param>
/// <param name="Earlier">The version just below the cleaned mod's; null for <see cref="NavmeshCase.Added"/>.</param>
/// <param name="Current">The cleaned mod's version; for a deletion, the earlier version with the deleted state.</param>
/// <param name="ReplacedBy">For <see cref="NavmeshCase.DeletedAndReplaced"/>: the added navmeshes that overlap it, in key order.</param>
/// <param name="Neighbours">
/// The navmeshes edge-linked to it in either direction, with their winning version, in key order; none when a later plugin changed
/// a part again, since it then never goes back.
/// </param>
/// <param name="Doors">The doors linked to it from either side, in key order.</param>
/// <param name="NamedInPreferredPaths">The winning navmesh info map's preferred paths name it.</param>
/// <param name="WinningMapMod">The plugin of the last navmesh info map in load order; null when no plugin has one.</param>
/// <param name="CellDeletedInAVersion">A version read of <paramref name="Cell"/> deletes the cell.</param>
/// <param name="LaterWinner">The winning version when a plugin after the cleaned mod wins the navmesh; null when the cleaned mod's version wins.</param>
/// <param name="WinningMapEntry">The entry of the last plugin whose navmesh info map carries one, whoever wins the navmesh record; null when none does.</param>
/// <remarks>When a later plugin deletes a navmesh the cleaned mod keeps, nothing in it goes back.</remarks>
public sealed record NavmeshChangedByCleanedMod(
    RecordKey Navmesh,
    RecordKey SpaceKey,
    RecordKey Cell,
    PluginName WinningMod,
    NavmeshCase Case,
    PartOutcome Geometry,
    PartOutcome Presence,
    PartOutcome MapEntry,
    NavmeshShape? Earlier,
    NavmeshShape Current,
    ImmutableArray<RecordKey> ReplacedBy,
    ImmutableArray<NeighbourNavmesh> Neighbours,
    ImmutableArray<DoorLinkFact> Doors,
    bool NamedInPreferredPaths,
    PluginName? WinningMapMod,
    bool CellDeletedInAVersion,
    NavmeshShape? LaterWinner,
    NavmeshMapEntryFact? WinningMapEntry)
{
    /// <summary>The version that wins, as the game shows it; its map entry is the one in force up to its own plugin's level.</summary>
    public NavmeshShape Winner => LaterWinner ?? Current;

    /// <summary>The cleaned mod changed its triangles or whether it is there; a navmesh whose only change is its map entry keeps its earlier triangles.</summary>
    public bool ChangesTriangles => Geometry is not PartOutcome.Unchanged || Presence is not PartOutcome.Unchanged;
}
