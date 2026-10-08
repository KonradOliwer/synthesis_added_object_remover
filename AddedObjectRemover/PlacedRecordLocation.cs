using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Where a target object's override is written: through its cell's winning context, into the same child list.</summary>
internal sealed record PlacedRecordLocation(
    IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> WinningCell,
    bool InPersistentList);
