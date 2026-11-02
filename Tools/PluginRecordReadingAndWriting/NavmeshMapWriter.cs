using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// Writes entries into the patch's override of a navmesh info map (NAVI). Each plugin's map carries only the entries of the navmeshes
/// it changes, so the override holds only the patch's own entries. Not thread-safe.
/// </summary>
internal sealed class NavmeshMapWriter(ISkyrimMod patchMod, RecordHandles handles)
{
    /// <remarks>
    /// The preferred paths, NVSI and navmesh version are copied unchanged from the winning map, so the game reads the same ones as
    /// without the patch; the FormVersion comes with them, as the NVSI bytes follow its record format.
    /// </remarks>
    public void WriteEntries(RecordKey map, ImmutableArray<NavmeshEntryOrder> entries)
    {
        var mapKey = map.ToFormKey();
        var winner = handles.NavmeshMaps.Last(version => version.Map.FormKey == mapKey).Map;
        var written = GetOrAddOverride(mapKey, winner);
        var (preferredPathing, nvsi, navMeshVersion) = MutagenCalls.Read(() =>
            (winner.PreferredPathing?.DeepCopy(), winner.NVSI is { } stored ? stored.ToArray() : null, winner.NavMeshVersion));
        written.FormVersion = winner.FormVersion;
        written.PreferredPathing = preferredPathing;
        written.NVSI = nvsi;
        written.NavMeshVersion = navMeshVersion;
        foreach (var order in entries) NavmeshRecordParts.MapEntry.CopyInto(written, EntryFor(mapKey, order));
        SortByNavmesh(written.MapInfos);
    }

    private NavigationMeshInfoMap GetOrAddOverride(FormKey mapKey, INavigationMeshInfoMapGetter winner)
    {
        if (patchMod.NavigationMeshInfoMaps.RecordCache.TryGetValue(mapKey, out var held)) return held;
        var copy = (NavigationMeshInfoMap)MutagenCalls.Read(() => winner.DeepCopy());
        copy.MapInfos.Clear();
        patchMod.NavigationMeshInfoMaps.Add(copy);
        return copy;
    }

    private NavigationMapInfo EntryFor(FormKey mapKey, NavmeshEntryOrder order)
    {
        var navmesh = order.Navmesh.ToFormKey();
        var source = handles.NavmeshMaps.Single(version => version.Plugin == order.From && version.Map.FormKey == mapKey).Map;
        var entry = MutagenCalls.Read(() => source.MapInfos.Last(info => info.NavigationMesh.FormKey == navmesh).DeepCopy());
        if (order.FlattenAndDetachZ is { } z) FlattenAndDetach(entry, z);
        return entry;
    }

    private static void FlattenAndDetach(NavigationMapInfo entry, float z)
    {
        entry.Point = entry.Point.WithZ(z);
        if (entry.Island is { } island)
        {
            island.Min = island.Min.WithZ(z);
            island.Max = island.Max.WithZ(z);
            for (var index = 0; index < island.Vertices.Count; index++) island.Vertices[index] = island.Vertices[index].WithZ(z);
        }
        entry.MergedTo.Clear();
        entry.PreferredMerges.Clear();
        entry.LinkedDoors.Clear();
    }

    private static void SortByNavmesh(ExtendedList<NavigationMapInfo> entries)
    {
        NavigationMapInfo[] sorted = [.. entries.OrderBy(entry => entry.NavigationMesh.FormKey.KeyAsWritten(), RecordKeyOrder.Comparer)];
        entries.Clear();
        entries.AddRange(sorted);
    }
}
