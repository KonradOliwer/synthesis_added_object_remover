using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Names the Location chains of cells through the load-order winners, following each chain once. A link that cannot be
/// found, or a parent that loops back, ends the chain there.
/// </summary>
internal sealed class LocationNameReader(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
{
    private readonly Dictionary<FormKey, FormKey?> _worldspaceTops = new();
    private readonly Dictionary<(FormKey Location, FormKey? Top), LocationNames?> _names = new();

    /// <param name="worldspace">The cell's worldspace; null for an interior.</param>
    public LocationNames? Read(ICellGetter cell, FormKey? worldspace)
    {
        if (!cell.Location.TryResolve(linkCache, out var location)) return null;
        var top = worldspace is { } worldspaceKey ? FindWorldspaceTop(worldspaceKey) : null;
        if (_names.TryGetValue((location.FormKey, top), out var names)) return names;
        names = NameChain(FollowParents(location), top);
        _names.Add((location.FormKey, top), names);
        return names;
    }

    private FormKey? FindWorldspaceTop(FormKey worldspace)
    {
        if (_worldspaceTops.TryGetValue(worldspace, out var top)) return top;
        top = linkCache.TryResolve<IWorldspaceGetter>(worldspace, out var record) && record.Location.TryResolve(linkCache, out var location)
            ? FollowParents(location)[^1].FormKey
            : null;
        _worldspaceTops.Add(worldspace, top);
        return top;
    }

    private List<ILocationGetter> FollowParents(ILocationGetter location)
    {
        List<ILocationGetter> chain = [location];
        var seen = new HashSet<FormKey> { location.FormKey };
        while (chain[^1].ParentLocation.TryResolve(linkCache, out var parent) && seen.Add(parent.FormKey)) chain.Add(parent);
        return chain;
    }

    /// <remarks>A place Location without any name leaves the cell unnamed by its Location; a hold without one is named like the place.</remarks>
    private static LocationNames? NameChain(List<ILocationGetter> chain, FormKey? top)
    {
        if (RecordDisplayNames.Of(chain[0].Name, chain[0].EditorID) is not { } place) return null;
        var topIndex = chain.FindIndex(location => location.FormKey == top);
        var hold = chain[Math.Max((topIndex < 0 ? chain.Count - 1 : topIndex) - 1, 0)];
        return new LocationNames(RecordDisplayNames.Of(hold.Name, hold.EditorID) ?? place, place);
    }
}
