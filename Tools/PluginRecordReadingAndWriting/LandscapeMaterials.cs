using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>The material records of landscape textures, answered from a link cache. Thread-safe, like the link cache.</summary>
internal sealed class LandscapeMaterials(ILinkCache linkCache, PluginNames names)
{
    public RecordKey[] ChainOf(RecordKey texture)
    {
        if (texture == RecordKey.Null || texture == RecordKey.None) return [];
        if (ReadLink<ILandscapeTextureGetter>(texture.ToFormKey(), landscapeTexture => landscapeTexture.MaterialType.FormKey) is not { } next) return [];
        var chain = new List<FormKey>();
        while (!next.IsNull && !chain.Contains(next) && ReadLink<IMaterialTypeGetter>(next, material => material.Parent.FormKey) is { } parent)
        {
            chain.Add(next);
            next = parent;
        }
        return [.. chain.Select(names.KeyOf)];
    }

    /// <param name="linkOf">Reads the link out of the record; holds Mutagen getters only.</param>
    /// <returns>Null when the record does not resolve.</returns>
    private FormKey? ReadLink<TRecord>(FormKey formKey, Func<TRecord, FormKey> linkOf)
        where TRecord : class, IMajorRecordQueryableGetter =>
        MutagenCalls.Read(() => linkCache.TryResolve<TRecord>(formKey, out var resolved) ? linkOf(resolved) : (FormKey?)null);
}
