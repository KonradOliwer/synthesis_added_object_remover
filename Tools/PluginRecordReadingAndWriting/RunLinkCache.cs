using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// One run's link cache over the final world, built when the run opens its plugin records and dropped with them. An immutable
/// cache indexes a record type when it is first asked for it, so a cache kept across the runs of a batch would miss the cell
/// and worldspace copies the earlier runs wrote into the patch.
/// </summary>
internal sealed record RunLinkCache(ILinkCache<ISkyrimMod, ISkyrimModGetter> Cache) : IRecordIndex;
