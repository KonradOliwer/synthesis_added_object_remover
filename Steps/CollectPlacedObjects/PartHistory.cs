using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>What the cleaned mod last changed in a part of a record, over the record's versions from the one just below the cleaned mod's up to the winner, the patch included.</summary>
internal static class PartHistory
{
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

    /// <summary>The outcome of one part of a record whose versions were read from the one just below the cleaned mod's.</summary>
    /// <param name="plugins">The plugin of each of <paramref name="versions"/>.</param>
    /// <param name="includes">Whether a set of parts includes the part.</param>
    public static PartOutcome Of<TParts>(
        ImmutableArray<VersionDifferences<TParts>> versions, IReadOnlyList<PluginName> plugins, PluginName patch, Func<TParts, bool> includes) =>
        Of(plugins, VersionComparison.NamedPluginIndex, patch, VersionComparison.Differs(versions, includes));

    /// <summary>
    /// As <see cref="Of{TParts}"/>; a part that would be undone but whose earlier value cannot be written into the winner is
    /// <see cref="PartOutcome.KeptForRecordFormat"/>.
    /// </summary>
    public static PartOutcome Of(PartDifferences part, PluginName patch)
    {
        var outcome = Of(part.Versions, PluginsOf(part.Versions), patch, static differs => differs);
        return outcome is PartOutcome.Undo && part.LowestNotCopyable ? new PartOutcome.KeptForRecordFormat() : outcome;
    }

    public static ImmutableArray<PluginName> PluginsOf<TParts>(ImmutableArray<VersionDifferences<TParts>> versions) => [.. versions.Select(version => version.Plugin)];

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
