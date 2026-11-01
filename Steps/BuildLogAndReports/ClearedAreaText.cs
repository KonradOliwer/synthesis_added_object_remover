using System.Diagnostics;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The words and numbers for the share of the cleaned mod's objects that still stand in an area.</summary>
internal static class ClearedAreaText
{
    private const int PercentDecimals = 1;

    /// <summary>How an area's weight is named: what the objects are weighed by.</summary>
    public static string MeasureName(ClearedAreaMeasure measure) => measure switch
    {
        ClearedAreaMeasure.ObjectSize => "size",
        ClearedAreaMeasure.ObjectCount => "count",
        _ => throw new UnreachableException($"Unknown area measure {measure}."),
    };

    /// <summary>
    /// "; 0 of 214 objects still stand (0.0% by size)" after a line's reason, with the measure the share was taken by; nothing for an
    /// area that was not judged or holds no object of the cleaned mod, which the reason already says.
    /// </summary>
    /// <param name="label">Says which area it is when a line names two, such as "at its old place "; empty otherwise.</param>
    public static string After(AreaShare? share, string label = "") =>
        share is { Verdict: not AreaVerdict.NoObjectsAround } counted
            ? $"; {label}{TextFormat.Count(counted.Standing)} of {TextFormat.Count(counted.Objects, "object", "objects")} still stand "
                + $"({TextFormat.Fixed(counted.StandingPercent, PercentDecimals)}% by {MeasureName(counted.Measure)})"
            : string.Empty;
}
