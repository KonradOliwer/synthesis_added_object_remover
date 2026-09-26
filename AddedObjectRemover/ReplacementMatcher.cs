using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

internal readonly record struct ReplacementLogEntry(
    FormKey OtherFormKey,
    string? OtherEditorId,
    ModKey OtherPlugin,
    FormKey TargetFormKey,
    string? TargetEditorId,
    float Distance,
    float SizeRatio);

/// <param name="LogEntries">Empty unless verbose.</param>
internal sealed record ReplacementResult(int ReplacedCount, IReadOnlyList<ReplacementLogEntry> LogEntries);

/// <summary>
/// An other-mod object counts as replaced (and is ignored by the too-close test) when a target
/// object in the same space sits within the position tolerance of it and every matching pair of
/// sorted scaled dimensions has a min/max ratio of at least the size similarity.
/// Matches are found in parallel per target and applied in target order, so each replaced
/// object is attributed to its first matching target.
/// </summary>
internal sealed class ReplacementMatcher
{
    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly IReadOnlyDictionary<FormKey, OtherObjectIndex> _indexes;
    private readonly BaseObjectShapeProvider _shapes;
    private readonly float _positionTolerance;
    private readonly float _sizeSimilarity;

    public ReplacementMatcher(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        BaseObjectShapeProvider shapes,
        float positionTolerance,
        float sizeSimilarity)
    {
        _targets = targets;
        _indexes = indexes;
        _shapes = shapes;
        _positionTolerance = positionTolerance;
        _sizeSimilarity = sizeSimilarity;
    }

    private readonly record struct Match(int OtherIndex, float Distance, float SizeRatio);

    public ReplacementResult MarkReplacedObjects(bool collectLog, ParallelOptions parallelOptions)
    {
        var matchesByTarget = new List<Match>?[_targets.Count];
        Parallel.For(0, _targets.Count, parallelOptions, t => matchesByTarget[t] = FindMatches(_targets[t]));
        return ApplyInTargetOrder(matchesByTarget, collectLog);
    }

    private List<Match>? FindMatches(TargetObject target)
    {
        if (!_indexes.TryGetValue(target.SpaceKey, out var index) || index.Count == 0) return null;
        if (ScaledSortedDims(_shapes.GetLocalBox(target.Base), target.Transform.Scale) is not { } targetDims) return null;

        var position = target.Transform.Position;
        var margin = new Vector3(_positionTolerance);
        var candidates = new List<int>();
        index.Grid.Collect(new Box(position - margin, position + margin), candidates);

        List<Match>? matches = null;
        foreach (var otherIndex in candidates)
        {
            var other = index[otherIndex];
            var distance = Vector3.Distance(other.Position, position);
            if (distance > _positionTolerance) continue;
            if (!index.TryGetVisibleCenter(otherIndex, out _)) continue;
            if (ScaledSortedDims(_shapes.GetLocalBox(other.Base), other.Scale) is not { } otherDims) continue;

            var ratio = SizeRatio(targetDims, otherDims);
            if (ratio < _sizeSimilarity) continue;

            (matches ??= []).Add(new Match(otherIndex, distance, ratio));
        }
        return matches;
    }

    private ReplacementResult ApplyInTargetOrder(List<Match>?[] matchesByTarget, bool collectLog)
    {
        var replacedCount = 0;
        var log = new List<ReplacementLogEntry>();
        for (var t = 0; t < _targets.Count; t++)
        {
            if (matchesByTarget[t] is not { } matches) continue;
            var target = _targets[t];
            var index = _indexes[target.SpaceKey];
            foreach (var match in matches)
            {
                if (!index.TryMarkReplaced(match.OtherIndex)) continue;
                replacedCount++;
                if (!collectLog) continue;
                var other = index[match.OtherIndex];
                log.Add(new ReplacementLogEntry(
                    other.FormKey, other.EditorId, other.WinningMod,
                    target.Record.FormKey, target.Record.EditorID,
                    match.Distance, match.SizeRatio));
            }
        }
        return new ReplacementResult(replacedCount, log);
    }

    /// <summary>
    /// Scaled dimensions sorted largest-first, or null when the box has no size at all (missing
    /// bounds come back as <see cref="Box.Zero"/>). Box sizes and normalized scales are never
    /// negative, so neither are the dimensions.
    /// </summary>
    private static (float A, float B, float C)? ScaledSortedDims(Box local, float scale)
    {
        if (local.Size == Vector3.Zero) return null;
        var size = local.Size * scale;
        var dims = new[] { size.X, size.Y, size.Z };
        Array.Sort(dims);
        return (dims[2], dims[1], dims[0]);
    }

    /// <summary>Smallest, across the three matching sorted-dimension pairs, of min(a,b)/max(a,b).</summary>
    private static float SizeRatio((float A, float B, float C) x, (float A, float B, float C) y) =>
        MathF.Min(MathF.Min(DimensionRatio(x.A, y.A), DimensionRatio(x.B, y.B)), DimensionRatio(x.C, y.C));

    private static float DimensionRatio(float a, float b)
    {
        if (a == 0 || b == 0) return a == b ? 1f : 0f;
        return MathF.Min(a, b) / MathF.Max(a, b);
    }
}
