using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The words and numbers for the share of the cleaned mod's objects that still stand in an area.</summary>
internal static class ClearedAreaText
{
    private const int PercentDecimals = 1;

    /// <summary>
    /// "; 0 of 214 objects still stand (0.0% by count, 0.0% by size)" or "; 1 of 1 object still stands (100.0% by count, 100.0% by
    /// size)" after a line's reason; nothing for an area that was not judged or holds no object of the cleaned mod, which the reason
    /// already says.
    /// </summary>
    /// <param name="label">Says which area it is when a line names two, such as "at its old place "; empty otherwise.</param>
    public static string After(AreaShare? share, string label = "") =>
        share is { Verdict: not AreaVerdict.NoObjectsAround } counted
            ? $"; {label}{TextFormat.Count(counted.Standing)} of {TextFormat.Count(counted.Objects, "object", "objects")} still {TextFormat.Agreeing(counted.Standing, "stands", "stand")} "
                + $"({TextFormat.Fixed(counted.CountPercent, PercentDecimals)}% by count, {TextFormat.Fixed(counted.SizePercent, PercentDecimals)}% by size)"
            : string.Empty;

    /// <summary>"in a cleared area: 2 of 140 of the cleaned mod's objects still stand (1.4%), 0.3% by size".</summary>
    public static string Reason(AreaShare share) =>
        $"in a cleared area: {TextFormat.Count(share.Standing)} of {TextFormat.Count(share.Objects)} of the cleaned mod's objects still "
        + $"{TextFormat.Agreeing(share.Standing, "stands", "stand")} ({TextFormat.Fixed(share.CountPercent, PercentDecimals)}%), "
        + $"{TextFormat.Fixed(share.SizePercent, PercentDecimals)}% by size";
}
