using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Reads the load order's collision layer records (COLL) at their winning versions.</summary>
internal static class CollisionLayerReader
{
    /// <returns>By layer number; of two records with one number, the one whose winner loads last.</returns>
    public static IReadOnlyDictionary<uint, CollisionLayerFact> Read(ILinkCache linkCache)
    {
        var winners = WinningLayers(linkCache).ConvertAll(ReadLayer);
        var indexOf = winners.ToDictionary(layer => layer.FormKey, layer => layer.Index);
        var byIndex = new Dictionary<uint, CollisionLayerFact>();
        foreach (var layer in winners)
        {
            byIndex.TryAdd(layer.Index, new CollisionLayerFact(
                layer.Index,
                (layer.Flags & (Mutagen.Bethesda.Skyrim.CollisionLayer.Flag.TriggerVolume | Mutagen.Bethesda.Skyrim.CollisionLayer.Flag.Sensor)) != 0,
                layer.Flags.HasFlag(Mutagen.Bethesda.Skyrim.CollisionLayer.Flag.NavmeshObstacle),
                [.. layer.CollidesWith.Where(indexOf.ContainsKey).Select(key => indexOf[key])]));
        }
        return byIndex;
    }

    /// <returns>Each record once, at its winning version, the plugins highest priority first.</returns>
    private static List<ICollisionLayerGetter> WinningLayers(ILinkCache linkCache)
    {
        var seen = new HashSet<FormKey>();
        return MutagenCalls.Read(() => linkCache.PriorityOrder.SelectMany(mod => mod.EnumerateMajorRecords<ICollisionLayerGetter>()).Where(layer => seen.Add(layer.FormKey)).ToList());
    }

    private static LayerRecord ReadLayer(ICollisionLayerGetter layer)
    {
        var (index, flags, collidesWith) = MutagenCalls.Read(() => (layer.Index, layer.Flags, (layer.CollidesWith ?? []).Select(link => link.FormKey).ToArray()));
        return new LayerRecord(layer.FormKey, index, flags, collidesWith);
    }

    /// <param name="CollidesWith">The records of the layers it collides with.</param>
    private sealed record LayerRecord(FormKey FormKey, uint Index, Mutagen.Bethesda.Skyrim.CollisionLayer.Flag Flags, FormKey[] CollidesWith);
}
