using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Another mod's object that a target object replaces.</summary>
/// <param name="By">The first matching target object.</param>
internal sealed record Replacement(OtherId Rival, TargetId By, float Distance, float SizeRatio);

/// <summary>The replaced rivals: objects that stay indexed but never match in the too-close test.</summary>
internal sealed class Replacements
{
    private readonly bool[] _replaced;

    private Replacements(int rivalCount, IReadOnlyList<Replacement> list)
    {
        _replaced = new bool[rivalCount];
        foreach (var replacement in list) _replaced[replacement.Rival.Index] = true;
        List = list;
    }

    /// <summary>In <see cref="TargetId"/> order, then in the order the matches were found.</summary>
    public IReadOnlyList<Replacement> List { get; }

    public int Count => List.Count;

    public static Replacements None(int rivalCount) => new(rivalCount, []);

    public static Replacements Of(int rivalCount, IReadOnlyList<Replacement> list) => new(rivalCount, list);

    /// <summary>False for ids beyond the rivals, such as backdrop objects, which are never replaced.</summary>
    public bool IsReplaced(OtherId id) => id.Index < _replaced.Length && _replaced[id.Index];
}

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
    private readonly IReadOnlyDictionary<FormKey, OtherObjectIndex> _indexes;
    private readonly BaseObjectShapeProvider _shapes;

    private ReplacementMatcher(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        BaseObjectShapeProvider shapes)
    {
        _targets = targets;
        _indexes = indexes;
        _shapes = shapes;
    }

    private readonly record struct Match(OtherId Rival, float Distance, float SizeRatio);

    /// <param name="rivalCount">The number of rivals of the world; the indexes hold rivals only.</param>
    public static Replacements Find(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        int rivalCount,
        BaseObjectShapeProvider shapes,
        ParallelOptions parallelOptions)
    {
        var matcher = new ReplacementMatcher(targets, indexes, shapes);
        var matchesByTarget = new List<Match>?[targets.Count];
        Parallel.For(0, targets.Count, parallelOptions, t => matchesByTarget[t] = matcher.FindMatches(targets[t]));
        return matcher.ApplyInTargetOrder(matchesByTarget, rivalCount);
    }

    private List<Match>? FindMatches(TargetObject target)
    {
        if (!_indexes.TryGetValue(target.SpaceKey, out var index) || index.Count == 0) return null;
        if (ScaledSortedDims(_shapes.GetLocalBox(target.Base), target.Transform.Scale) is not { } targetDims) return null;

        var position = target.Transform.Position;
        var margin = new Vector3(PositionTolerance);
        var candidates = new List<int>();
        index.PositionGrid.Collect(new Box(position - margin, position + margin), candidates);

        List<Match>? matches = null;
        foreach (var slot in candidates)
        {
            var other = index[slot];
            var distance = Vector3.Distance(other.Position, position);
            if (distance > PositionTolerance) continue;
            if (!index.IsVisible(slot)) continue;
            if (ScaledSortedDims(_shapes.GetLocalBox(other.Base), other.Scale) is not { } otherDims) continue;

            var ratio = SizeRatio(targetDims, otherDims);
            if (ratio < SizeSimilarity) continue;

            (matches ??= []).Add(new Match(other.Id, distance, ratio));
        }
        return matches;
    }

    private Replacements ApplyInTargetOrder(List<Match>?[] matchesByTarget, int rivalCount)
    {
        var claimed = new HashSet<OtherId>();
        var list = new List<Replacement>();
        for (var t = 0; t < _targets.Count; t++)
        {
            if (matchesByTarget[t] is not { } matches) continue;
            foreach (var match in matches)
            {
                if (claimed.Add(match.Rival)) list.Add(new Replacement(match.Rival, new TargetId(t), match.Distance, match.SizeRatio));
            }
        }
        return Replacements.Of(rivalCount, list);
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
