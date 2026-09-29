using System.Numerics;

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
    private readonly IRivalPositions _rivals;
    private readonly ShapeCatalog _shapes;

    private ReplacementMatcher(IReadOnlyList<TargetObject> targets, IRivalPositions rivals, ShapeCatalog shapes)
    {
        _targets = targets;
        _rivals = rivals;
        _shapes = shapes;
    }

    private readonly record struct Match(OtherId Rival, float Distance, float SizeRatio);

    /// <param name="rivalCount">The number of rivals of the world.</param>
    public static Replacements Find(
        IReadOnlyList<TargetObject> targets,
        IRivalPositions rivals,
        int rivalCount,
        ShapeCatalog shapes,
        WorkOrder order,
        ParallelOptions parallelOptions)
    {
        var matcher = new ReplacementMatcher(targets, rivals, shapes);
        var matchesByTarget = ParallelMap.Run(
            parallelOptions,
            order,
            targets.Count,
            () => new List<OtherId>(),
            (t, nearby) => matcher.FindMatches(targets[t], nearby));
        return matcher.ApplyInTargetOrder(matchesByTarget, rivalCount);
    }

    /// <param name="nearby">The calling worker's buffer.</param>
    private List<Match>? FindMatches(TargetObject target, List<OtherId> nearby)
    {
        if (ScaledSortedDims(_shapes.GetLocalBox(target.Base), target.Transform.Scale) is not { } targetDims) return null;

        var position = target.Transform.Position;
        _rivals.Within(target.SpaceKey, position, PositionTolerance, nearby);

        List<Match>? matches = null;
        foreach (var id in nearby)
        {
            var other = _rivals.Get(id);
            var distance = Vector3.Distance(other.Position, position);
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
