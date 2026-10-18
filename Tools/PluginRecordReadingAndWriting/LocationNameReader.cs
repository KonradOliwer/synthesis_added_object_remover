using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Names Location chains through the load-order winners. A link that cannot be found, or a parent that loops back, ends the
/// chain there. Holds no state, so it may be called from several threads at once.
/// </summary>
/// <param name="holdKeyword">The keyword that marks a Location as a hold.</param>
internal sealed class LocationNameReader(ILinkCache linkCache, FormKey holdKeyword)
{
    /// <returns>The last Location of the chain that starts at the worldspace's own Location; null when it has none.</returns>
    public FormKey? FindWorldspaceTop(FormKey worldspace) =>
        linkCache.TryResolve<IWorldspaceGetter>(worldspace, out var record) && record.Location.TryResolve(linkCache, out var location)
            ? FollowParents(location)[^1].FormKey
            : null;

    /// <param name="top">The top Location of the cell's worldspace; null for an interior or a worldspace without one.</param>
    public LocationNames? Read(FormKey location, FormKey? top) =>
        linkCache.TryResolve<ILocationGetter>(location, out var record) ? NameChain(FollowParents(record), top) : null;

    private List<ILocationGetter> FollowParents(ILocationGetter location)
    {
        List<ILocationGetter> chain = [location];
        var seen = new HashSet<FormKey> { location.FormKey };
        while (chain[^1].ParentLocation.TryResolve(linkCache, out var parent) && seen.Add(parent.FormKey)) chain.Add(parent);
        return chain;
    }

    /// <remarks>A place Location without any name leaves the cell unnamed by its Location; a hold without one is named like the place.</remarks>
    private LocationNames? NameChain(List<ILocationGetter> chain, FormKey? top)
    {
        if (RecordDisplayNames.Of(chain[0].Name, chain[0].EditorID) is not { } place) return null;
        var hold = FindHold(chain, top);
        return new LocationNames(RecordDisplayNames.Of(hold.Name, hold.EditorID) ?? place, place);
    }

    /// <remarks>
    /// The nearest Location with the hold keyword. Without it, the hold is the Location just below the top.
    /// </remarks>
    private ILocationGetter FindHold(List<ILocationGetter> chain, FormKey? top)
    {
        var keyworded = chain.Find(location => location.Keywords?.Any(keyword => keyword.FormKey == holdKeyword) == true);
        if (keyworded is not null) return keyworded;
        var topIndex = chain.FindIndex(location => location.FormKey == top);
        return chain[Math.Max((topIndex < 0 ? chain.Count - 1 : topIndex) - 1, 0)];
    }
}
