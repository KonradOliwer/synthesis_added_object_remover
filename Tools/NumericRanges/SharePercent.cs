namespace AddedObjectRemover;

/// <summary>Whether a part reaches a percentage of a whole, compared as <c>part * 100 &gt;= percent * whole</c> so no share is divided out and rounded.</summary>
public static class SharePercent
{
    public static bool AtLeast(int part, int whole, int percent) => part * Percent.PerWhole >= percent * whole;

    public static bool AtLeast(float part, float whole, int percent) => part * Percent.PerWhole >= percent * whole;
}
