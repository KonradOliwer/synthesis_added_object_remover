namespace AddedObjectRemover;

/// <summary>One plugin's own LAND record of an exterior cell.</summary>
/// <param name="Plugin">The plugin that holds this version.</param>
/// <param name="Heights">Row by row (<see cref="TerrainHeights.VerticesPerSide"/> per row); null where the record is deleted or has no height map.</param>
/// <param name="Layers">The texture layers; null where the record is deleted or the read scope did not ask for the layers.</param>
public sealed record TerrainVersion(PluginName Plugin, float[]? Heights, TerrainLayers? Layers);
