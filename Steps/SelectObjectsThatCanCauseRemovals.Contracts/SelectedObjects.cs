namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

/// <summary>What the select step hands on: the objects a target replaces, and the other mods' objects that can cause removals.</summary>
public sealed record SelectedObjects(Replacements Replacements, IObjectsThatCanCauseRemovals ObjectsThatCanCauseRemovals);
