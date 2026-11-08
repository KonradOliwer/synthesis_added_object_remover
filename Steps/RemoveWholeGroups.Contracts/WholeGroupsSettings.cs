namespace AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

/// <param name="GapInUnits">Two of the cleaned mod's objects belong to one group when their boxes come this close horizontally; groups chain.</param>
public sealed record WholeGroupsSettings(float GapInUnits);
