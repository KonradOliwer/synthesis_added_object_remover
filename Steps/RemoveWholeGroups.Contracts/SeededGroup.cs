using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

/// <summary>A group of the cleaned mod's objects that holds a clash removal.</summary>
/// <param name="Members">Every object of the group, removed or standing, ascending.</param>
/// <param name="Seed">
/// The lowest-id object that seeded the group: a member an earlier step took from its place, or another mod's object the cleaned mod
/// changed that an earlier step took from its place, whose earlier place lies within the gap of a member.
/// </param>
/// <param name="FootprintMin">The smallest X and Y of the members' boxes.</param>
/// <param name="FootprintMax">The largest X and Y of the members' boxes.</param>
public sealed record SeededGroup(ImmutableArray<TargetId> Members, TargetId Seed, RecordKey Space, Vector2 FootprintMin, Vector2 FootprintMax);
