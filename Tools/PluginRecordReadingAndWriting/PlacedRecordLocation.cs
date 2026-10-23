using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Where a target object's override is written: its winning cell as scanned and the child list it was found in. The scanned
/// cell serves reading and names the cell; the cell that is overridden is resolved again through the Synthesis cache.
/// </summary>
internal sealed record PlacedRecordLocation(ICellGetter WinningCell, bool InPersistentList);
