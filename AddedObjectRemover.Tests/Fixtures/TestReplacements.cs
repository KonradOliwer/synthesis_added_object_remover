namespace AddedObjectRemover.Tests.Fixtures;

internal static class TestReplacements
{
    /// <summary>No other-mod object is replaced.</summary>
    public static Replacements None(int otherModObjectCount) => Replacements.Of(otherModObjectCount, []);
}
