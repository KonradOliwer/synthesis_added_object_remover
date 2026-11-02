using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>The patch's cell overrides, each created on first use; every writer of one patch writing shares them. Not thread-safe.</summary>
internal sealed class CellOverrides(SynthesisWriteCache synthesis, ISkyrimMod patchMod)
{
    /// <summary>The patch's cell overrides given out, by cell FormKey.</summary>
    private readonly Dictionary<FormKey, ICell> _cells = [];

    public bool PatchHolds(FormKey cellKey) =>
        _cells.ContainsKey(cellKey) || (synthesis.Cache.TryResolveContext<ICell, ICellGetter>(cellKey, out var context) && context.ModKey == patchMod.ModKey);

    /// <remarks>
    /// The cell's context comes from the Synthesis cache, so an exterior cell's worldspace is copied from a plugin above the patch,
    /// and a cell copy an earlier run wrote is reused. A new override is a copy of the winning cell without its children. A deleted
    /// winning cell throws: no caller asks to write into one.
    /// </remarks>
    public ICell GetOrAdd(FormKey cellKey)
    {
        if (_cells.TryGetValue(cellKey, out var cell)) return cell;
        var context = synthesis.Cache.ResolveContext<ICell, ICellGetter>(cellKey);
        if (context.Record.IsDeleted) throw new InvalidOperationException($"Cell {cellKey} is deleted in the load order; nothing is written into it.");
        cell = MutagenCalls.Read(() => context.GetOrAddAsOverride(patchMod));
        _cells[cellKey] = cell;
        return cell;
    }
}
