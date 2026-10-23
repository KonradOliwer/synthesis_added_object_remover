namespace AddedObjectRemover;

/// <summary>One plugin's version of a placed record: where it stands, what it shows and whether the game has it. It is not judged here.</summary>
/// <param name="Plugin">The plugin that holds this version.</param>
/// <param name="Scale">As the version stores it; null when it has none.</param>
/// <param name="Base">The version's base object; null when its base link is empty.</param>
public readonly record struct PlacedVersionFact(PluginName Plugin, PlacementFacts Placement, float? Scale, BaseKey? Base);
