namespace AddedObjectRemover;

/// <summary>Whether a part reaches or stays within a percentage of a whole, compared as <c>part * 100 &gt;= percent * whole</c> so no share is divided out and rounded.</summary>
public static class SharePercent
{
    public static bool AtLeast(int part, int whole, int percent) => part * Percent.PerWhole >= percent * whole;

    public static bool AtLeast(float part, float whole, int percent) => part * Percent.PerWhole >= percent * whole;

    /// <param name="percent">A percentage that may be fractional, such as 0.1 for a tenth of a percent.</param>
    public static bool AtMost(double part, double whole, double percent) => part * Percent.PerWhole <= percent * whole;

    /// <returns>The percent the part is of the whole; 0 when the whole is nothing.</returns>
    public static double Of(double part, double whole) => whole > 0 ? part / whole * Percent.PerWhole : 0;
}
