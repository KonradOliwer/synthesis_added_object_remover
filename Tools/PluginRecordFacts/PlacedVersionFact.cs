namespace AddedObjectRemover;

/// <summary>Where one plugin's version of a placed record sits in that plugin's cell tree.</summary>
/// <param name="Space">The worldspace of the cell, or the interior cell itself.</param>
/// <param name="Cell">The cell whose child list holds the version.</param>
/// <param name="InPersistentList">The version is in the cell's persistent child list, not its temporary one.</param>
public readonly record struct VersionLocation(RecordKey Space, RecordKey Cell, bool InPersistentList);

/// <summary>One plugin's version of a placed record: where it stands, what it shows and whether the game has it. It is not judged here.</summary>
/// <param name="Plugin">The plugin that holds this version.</param>
/// <param name="Scale">As the version stores it; null when it has none.</param>
/// <param name="Base">The version's base object; null when its base link is empty.</param>
/// <param name="Location">Where the plugin keeps this version.</param>
public readonly record struct PlacedVersionFact(PluginName Plugin, PlacementFacts Placement, float? Scale, BaseKey? Base, VersionLocation Location);
