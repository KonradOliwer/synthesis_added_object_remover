namespace AddedObjectRemover;

/// <summary>What to do with a part of a record the cleaned mod may have changed.</summary>
public abstract record PartOutcome
{
    /// <summary>The cleaned mod is the last to change the part, or the patch is and holds the cleaned mod's value.</summary>
    public sealed record Undo : PartOutcome;

    /// <summary>The cleaned mod changed the part, but a later plugin or the patch changed it again; names the last of them.</summary>
    public sealed record LeftToLaterPlugin(PluginName Plugin) : PartOutcome;

    /// <summary>The cleaned mod did not change the part; a value it only carries over from below is not a change.</summary>
    public sealed record Unchanged : PartOutcome;

    /// <summary>
    /// The part would be undone, but its earlier value holds bytes Mutagen does not read, stored in another record format than the
    /// winner's, so it cannot be written back and stays. Given by the judging of a record's parts, never by <see cref="PartHistory"/>.
    /// </summary>
    public sealed record KeptForRecordFormat : PartOutcome;
}

public static class PartHistory
{
    /// <summary>The versions judged start just below the target plugin's, so its own version is the second.</summary>
    public const int TargetVersionIndex = 1;

    /// <param name="plugins">The plugin of each version of the record, lowest first; the version just below the cleaned mod's lies at <paramref name="cleanedModIndex"/> - 1.</param>
    /// <param name="cleanedModIndex">The index of the cleaned mod's version, at least 1.</param>
    /// <param name="patch">The patch, whose version holds what earlier patchers of the group wrote.</param>
    /// <param name="differs">Whether the values of two versions differ, given the lower index first.</param>
    /// <remarks>
    /// A real plugin that sets the cleaned mod's value back changed the part itself, so the part is left to it. The patch holding
    /// the cleaned mod's value is an earlier patcher's restore to the version below its own cleaned mod, so the part is undone.
    /// </remarks>
    public static PartOutcome Of(IReadOnlyList<PluginName> plugins, int cleanedModIndex, PluginName patch, Func<int, int, bool> differs)
    {
        if (!differs(cleanedModIndex - 1, cleanedModIndex)) return new PartOutcome.Unchanged();
        if (LastChangerAbove(plugins.Count, cleanedModIndex, differs) is not { } lastChanger) return new PartOutcome.Undo();
        if (plugins[lastChanger] == patch && !differs(cleanedModIndex, lastChanger)) return new PartOutcome.Undo();
        return new PartOutcome.LeftToLaterPlugin(plugins[lastChanger]);
    }

    /// <summary>
    /// The highest version above the cleaned mod's that differs from the one below it. Steps each within the tolerance can still add
    /// up to a change, so without such a step it is the highest version that differs from the cleaned mod's.
    /// </summary>
    /// <returns>Null when no version above the cleaned mod's changed the part.</returns>
    private static int? LastChangerAbove(int versionCount, int cleanedModIndex, Func<int, int, bool> differs)
    {
        if (LastChangingVersion.Of(versionCount, differs) is { } index && index > cleanedModIndex) return index;
        for (var drifted = versionCount - 1; drifted > cleanedModIndex; drifted--)
        {
            if (differs(cleanedModIndex, drifted)) return drifted;
        }
        return null;
    }
}
