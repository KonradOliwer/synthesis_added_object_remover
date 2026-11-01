using System.Diagnostics;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The words and numbers for the share of the cleaned mod's objects that still stand in an area.</summary>
internal static class ClearedAreaText
{
    private const int PercentDecimals = 1;
    private const double Hundred = 100d;

    /// <summary>How the area's weight is named: what the setting weighs the objects by.</summary>
    public static string MeasureName(ClearedAreaMeasure measure) => measure switch
    {
        ClearedAreaMeasure.ObjectSize => "size",
        ClearedAreaMeasure.ObjectCount => "count",
        _ => throw new UnreachableException($"Unknown area measure {measure}."),
    };

    /// <returns>The percent of the area's weight that still stands; 0 when the area has no weight.</returns>
    public static double StandingPercent(AreaShare share, ClearedAreaMeasure measure)
    {
        var (standing, all) = measure == ClearedAreaMeasure.ObjectSize ? (share.StandingSize, share.Size) : (share.Standing, (double)share.Objects);
        return all > 0 ? standing / all * Hundred : 0;
    }

    /// <summary>
    /// "; 0 of 214 objects still stand (0.0% by size)" after a line's reason; nothing for an area that was not judged or holds no
    /// object of the cleaned mod, which the reason already says.
    /// </summary>
    /// <param name="label">Says which area it is when a line names two, such as "at its old place "; empty otherwise.</param>
    public static string After(AreaShare? share, ClearedAreaMeasure measure, string label = "") =>
        share is { Verdict: not AreaVerdict.NoObjectsAround } counted
            ? $"; {label}{TextFormat.Count(counted.Standing)} of {TextFormat.Count(counted.Objects, "object", "objects")} still stand "
                + $"({TextFormat.Fixed(StandingPercent(counted, measure), PercentDecimals)}% by {MeasureName(measure)})"
            : string.Empty;

    /// <summary>The areas of the spaces one terrain quarter is judged in, added up under the verdict they gave together.</summary>
    public static AreaShare Sum(IEnumerable<AreaShare> areas, AreaVerdict verdict)
    {
        var list = areas.ToList();
        return new AreaShare(list.Sum(area => area.Objects), list.Sum(area => area.Standing), list.Sum(area => area.Size), list.Sum(area => area.StandingSize), verdict);
    }
}
