namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

/// <summary>When the area around a point counts as cleared of the cleaned mod's drawn objects.</summary>
/// <param name="SearchRadius">How far, horizontally, around a judged point the cleaned mod's objects count as its area.</param>
/// <param name="MaxStandingSizePercent">The share of the area's objects by size, in percent (0.1 is a tenth of a percent), that may still stand.</param>
/// <param name="MaxStandingCountPercent">The share of the area's objects by number, in percent, that may still stand.</param>
public sealed record ClearedArea(float SearchRadius, double MaxStandingSizePercent, double MaxStandingCountPercent);
