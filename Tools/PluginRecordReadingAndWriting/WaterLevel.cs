using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Reads an exterior cell's water fields by the game's rules for them.</summary>
internal static class WaterLevel
{
    /// <summary>The water height a cell stores to use its worldspace's default water height instead (xEdit shows it as "Default").</summary>
    private const float UseWorldspaceDefault = float.MaxValue;

    /// <remarks>A cell with "has water" and no stored height also uses its worldspace's default water height.</remarks>
    public static CellWaterCopy ReadCopy(PluginName plugin, ICellGetter cell) =>
        new(plugin, cell.Flags.HasFlag(Cell.Flag.HasWater), cell.WaterHeight is { } height && height != UseWorldspaceDefault ? height : null);
}
