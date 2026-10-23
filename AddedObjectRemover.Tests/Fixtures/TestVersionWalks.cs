namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Stop rules for the version walk of the placed record read.</summary>
internal static class TestVersionWalks
{
    /// <summary>No plugin stops the walk, so every version of a record is kept.</summary>
    public static readonly Func<PluginName, bool> NoStop = _ => false;
}
