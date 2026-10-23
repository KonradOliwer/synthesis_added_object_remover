using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// The Synthesis state's link cache: the plugins above the patch with the patch on top, so it also holds the cell copies
/// earlier runs wrote. Writing resolves the cells it overrides through it, never through a scanned context, so the patch's
/// masters stay above the patch.
/// </summary>
public sealed record SynthesisWriteCache(ILinkCache<ISkyrimMod, ISkyrimModGetter> Cache);
