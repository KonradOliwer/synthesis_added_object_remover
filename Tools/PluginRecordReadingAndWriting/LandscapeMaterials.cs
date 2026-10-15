using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>The material records of landscape textures, answered from a link cache. Thread-safe, like the link cache.</summary>
internal sealed class LandscapeMaterials(ILinkCache linkCache, PluginNames names)
{
    public RecordKey[] ChainOf(RecordKey texture)
    {
        if (texture == RecordKey.Null || texture == RecordKey.None) return [];
        if (!linkCache.TryResolve<ILandscapeTextureGetter>(texture.ToFormKey(), out var landscapeTexture)) return [];
        var chain = new List<FormKey>();
        var next = landscapeTexture.MaterialType.FormKey;
        while (!next.IsNull && !chain.Contains(next) && linkCache.TryResolve<IMaterialTypeGetter>(next, out var material))
        {
            chain.Add(next);
            next = material.Parent.FormKey;
        }
        return [.. chain.Select(names.KeyOf)];
    }
}
