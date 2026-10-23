namespace AddedObjectRemover;

/// <summary>Why the game's plugin list cannot be used. The caller words it; this holds the facts only.</summary>
public abstract record PluginListProblem
{
    /// <summary>The place of the plugins file cannot be determined.</summary>
    public sealed record PathUnknown : PluginListProblem;

    public sealed record FileMissing(string Path) : PluginListProblem;

    public sealed record MalformedLine(string Path, int Number) : PluginListProblem;

    /// <summary>A plugin of the Synthesis state has no entry in the plugin list.</summary>
    public sealed record PluginNotListed(string Path, PluginName Plugin) : PluginListProblem;

    /// <summary>A plugin of the Synthesis state, which Synthesis loads above the patch, is listed after the patch's entry.</summary>
    public sealed record PluginListedAfterPatch(string Path, PluginName Plugin, PluginName Patch) : PluginListProblem;
}
