using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>How each target object looks in game.</summary>
/// <param name="ByTarget">By <see cref="TargetId"/>.</param>
internal sealed record TargetLooks(ImmutableArray<ObjectVisibility> ByTarget);
