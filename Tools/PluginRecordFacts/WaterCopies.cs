namespace AddedObjectRemover;

/// <summary>Every plugin's own water fields of an exterior cell and of the cell's worldspace, each in load order (lowest priority first).</summary>
/// <param name="Cell">
/// Every plugin's copy of the cell; a plugin with two cells at the cell's grid has two copies, the one the game shows last.
/// </param>
/// <param name="Worldspace">Every plugin's copy of the cell's worldspace.</param>
public sealed record WaterCopies(CellWaterCopy[] Cell, WorldspaceWaterCopy[] Worldspace);

/// <summary>One plugin's own copy of an exterior cell's water fields.</summary>
/// <param name="HasWater">The cell's "has water" flag; without it the cell has no water, whatever height it stores.</param>
/// <param name="OwnLevel">The cell's own water height; null where the cell uses its worldspace's default water height.</param>
public sealed record CellWaterCopy(PluginName Plugin, bool HasWater, float? OwnLevel);

/// <summary>One plugin's own copy of a worldspace's default water height.</summary>
/// <param name="DefaultLevel">Null where the copy stores no default water height.</param>
public sealed record WorldspaceWaterCopy(PluginName Plugin, float? DefaultLevel);
