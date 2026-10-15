using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>A plugin's own LAND record of a cell, not yet decoded.</summary>
internal readonly record struct LandscapeVersion(PluginName Plugin, ILandscapeGetter Landscape);
