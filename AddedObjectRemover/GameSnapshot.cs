using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>The load-order records behind the target objects, by <see cref="TargetId"/>; only the patch writer and relocation's home cell read them.</summary>
internal sealed class RecordHandles(ImmutableArray<IPlacedGetter> records, ImmutableArray<TargetLocation> locations)
{
    public IPlacedGetter RecordOf(TargetId id) => records[id.Index];

    public TargetLocation LocationOf(TargetId id) => locations[id.Index];
}

/// <summary>What the scan of the load order produced: the world and the load-order data behind it.</summary>
/// <param name="Terrain">The terrain of the target worldspaces; empty unless objects of any plugin are needed as supporters or obstacles.</param>
/// <param name="Navmeshes">The winning navmeshes of each target space, not yet decoded; empty unless kept markers are moved.</param>
internal sealed record GameSnapshot(
    World World,
    RecordHandles Handles,
    TerrainHeights Terrain,
    IReadOnlyDictionary<FormKey, List<CellNavmesh>> Navmeshes);
