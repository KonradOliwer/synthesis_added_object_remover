namespace AddedObjectRemover;

/// <summary>A cell whose winning version deletes it.</summary>
/// <param name="DeletedBy">The plugin of that version.</param>
public sealed record DeletedCell(CellFact Cell, PluginName DeletedBy);
