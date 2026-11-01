using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Reads the load order's collision layer records (COLL) at their winning versions.</summary>
public static class CollisionLayerReader
{
    /// <returns>By layer number; of two records with one number, the one whose winner loads last.</returns>
    public static IReadOnlyDictionary<uint, CollisionLayerFact> Read(ILinkCache linkCache)
    {
        var winners = WinningLayers(linkCache);
        var indexOf = winners.ToDictionary(layer => layer.FormKey, layer => layer.Index);
        var byIndex = new Dictionary<uint, CollisionLayerFact>();
        foreach (var layer in winners)
        {
            byIndex.TryAdd(layer.Index, new CollisionLayerFact(
                layer.Index,
                (layer.Flags & (Mutagen.Bethesda.Skyrim.CollisionLayer.Flag.TriggerVolume | Mutagen.Bethesda.Skyrim.CollisionLayer.Flag.Sensor)) != 0,
                layer.Flags.HasFlag(Mutagen.Bethesda.Skyrim.CollisionLayer.Flag.NavmeshObstacle),
                [.. (layer.CollidesWith ?? []).Where(link => indexOf.ContainsKey(link.FormKey)).Select(link => indexOf[link.FormKey])]));
        }
        return byIndex;
    }

    /// <returns>Each record once, at its winning version, the plugins highest priority first.</returns>
    private static List<ICollisionLayerGetter> WinningLayers(ILinkCache linkCache)
    {
        var seen = new HashSet<FormKey>();
        return [.. linkCache.PriorityOrder.SelectMany(mod => mod.EnumerateMajorRecords<ICollisionLayerGetter>()).Where(layer => seen.Add(layer.FormKey))];
    }
}
