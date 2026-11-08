using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

/// <summary>The step's reason: the object belongs to a group of the cleaned mod's objects that holds a clash removal.</summary>
/// <param name="Seed">The group's seed, see <see cref="SeededGroup.Seed"/>.</param>
/// <param name="GroupSize">How many of the cleaned mod's objects the group holds, removed or standing.</param>
public sealed record InGroupWithClash(TargetId Seed, int GroupSize) : RemovalReason;
