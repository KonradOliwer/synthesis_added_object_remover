using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;

/// <summary>
/// An other-mod object counts as replaced (and is ignored by the too-close test) when a target
/// object in the same space sits within the position tolerance of it and every matching pair of
/// sorted scaled dimensions has a min/max ratio of at least the size similarity.
/// Matches are found in parallel per target and applied in target order, so each replaced
/// object is attributed to its first matching target.
/// </summary>
internal sealed class ReplacementMatcher
{
    /// <summary>Largest distance between a target object and another mod's object for the other one to count as replaced.</summary>
    private const float PositionTolerance = 16f;

    /// <summary>Smallest min/max ratio of each pair of sorted scaled dimensions for another mod's object to count as replaced.</summary>
    private const float SizeSimilarity = 0.75f;

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly IOtherModObjectPositions _otherModObjects;
    private readonly IBaseObjectShapes _shapes;

    private ReplacementMatcher(IReadOnlyList<TargetObject> targets, IOtherModObjectPositions otherModObjects, IBaseObjectShapes shapes)
    {
        _targets = targets;
        _otherModObjects = otherModObjects;
        _shapes = shapes;
    }

    private readonly record struct Match(OtherId OtherModObject, float Distance, float SizeRatio);

    /// <param name="otherModObjectCount">The number of other-mod objects of the world.</param>
    public static Replacements Find(
        IReadOnlyList<TargetObject> targets,
        IOtherModObjectPositions otherModObjects,
        int otherModObjectCount,
        IBaseObjectShapes shapes,
        WorkOrder order,
        Execution execution)
    {
        var matcher = new ReplacementMatcher(targets, otherModObjects, shapes);
        var matchesByTarget = ParallelMap.Run(
            execution,
            order,
            targets.Count,
            () => new List<OtherId>(),
            (t, nearby) => matcher.FindMatches(targets[t], nearby),
            ParallelMap.AutomaticRangeSize);
        return matcher.ApplyInTargetOrder(matchesByTarget, otherModObjectCount);
    }

    /// <param name="nearby">The calling worker's buffer.</param>
    private List<Match>? FindMatches(TargetObject target, List<OtherId> nearby)
    {
        if (ScaledSortedDims(_shapes.Of(target.Base).Box, target.Transform.Scale) is not { } targetDims) return null;

        var position = target.Transform.Position;
        _otherModObjects.Within(target.SpaceKey, position, PositionTolerance, nearby);

        List<Match>? matches = null;
        foreach (var id in nearby)
        {
            var other = _otherModObjects.Get(id);
            var distance = Vector3.Distance(other.Position, position);
            if (ScaledSortedDims(_shapes.Of(other.Base).Box, other.Scale) is not { } otherDims) continue;

            var ratio = SizeRatio(targetDims, otherDims);
            if (ratio < SizeSimilarity) continue;

            (matches ??= []).Add(new Match(other.Id, distance, ratio));
        }
        return matches;
    }

    private Replacements ApplyInTargetOrder(List<Match>?[] matchesByTarget, int otherModObjectCount)
    {
        var claims = Enumerable.Range(0, _targets.Count).SelectMany(
            t => matchesByTarget[t] ?? [],
            (t, match) => new Replacement(match.OtherModObject, new TargetId(t), match.Distance, match.SizeRatio));
        return Replacements.Of(otherModObjectCount, KeyedGroups.DistinctBy(claims, claim => claim.OtherModObject, EqualityComparer<OtherId>.Default));
    }

    /// <summary>
    /// Scaled dimensions sorted largest-first, or null when the box has no size at all (missing
    /// bounds come back as <see cref="Box.Zero"/>). Box sizes and normalized scales are never
    /// negative, so neither are the dimensions.
    /// </summary>
    private static SortedDimensions? ScaledSortedDims(Box local, float scale) =>
        local.Size == Vector3.Zero ? null : Boxes.SortedScaledDimensions(local, scale);

    /// <summary>Smallest, across the three matching sorted-dimension pairs, of min(a,b)/max(a,b).</summary>
    private static float SizeRatio(SortedDimensions x, SortedDimensions y) =>
        MathF.Min(
            MathF.Min(DimensionRatio(x.Largest, y.Largest), DimensionRatio(x.Middle, y.Middle)),
            DimensionRatio(x.Smallest, y.Smallest));

    private static float DimensionRatio(float a, float b)
    {
        if (a == 0 || b == 0) return a == b ? 1f : 0f;
        return MathF.Min(a, b) / MathF.Max(a, b);
    }
}
