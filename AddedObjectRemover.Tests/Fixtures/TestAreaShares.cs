namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Areas judged by the restore step for tests that need only the verdict.</summary>
internal static class TestAreaShares
{
    /// <summary>One object of the cleaned mod, removed, weighed by size, under the given verdict.</summary>
    public static AreaShare Of(AreaVerdict verdict) =>
        new(Objects: 1, Standing: 0, Size: 1, StandingSize: 0, verdict, ClearedAreaMeasure.ObjectSize, StandingPercent: 0);

    /// <summary>A terrain quarter whose area gives the verdict.</summary>
    public static LandQuarterJudged Quarter(AreaVerdict verdict) => new(Of(verdict));
}
