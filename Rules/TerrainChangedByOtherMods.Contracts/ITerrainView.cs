namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

/// <summary>The terrain and water the changed-terrain rule judges, as the next patcher reads them. Thread-safe.</summary>
public interface ITerrainView
{
    /// <summary>Every plugin's own land record of an exterior cell, decoded.</summary>
    /// <returns>In load order (lowest priority first); empty where no plugin has a land record for the cell.</returns>
    TerrainVersion[] ReadTerrainVersions(ExteriorCell cell);

    /// <summary>Every plugin's own water fields of an exterior cell and of the worldspace that supplies its default water.</summary>
    WaterCopies ReadWaterCopies(ExteriorCell cell);

    /// <summary>The material records of a landscape texture, from its own material up through each parent to the root.</summary>
    /// <returns>Empty where the texture or its material link does not resolve.</returns>
    RecordKey[] ReadTextureMaterialChain(RecordKey texture);

    /// <returns>The worldspace whose land the space uses; null where the space holds no land data.</returns>
    RecordKey? FindLandWorldspace(RecordKey space);
}
