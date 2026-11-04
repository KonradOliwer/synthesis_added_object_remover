using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>What the cleaned mod last changed in a part of a record, over the record's versions from the one just below the cleaned mod's up to the winner, the patch included.</summary>
internal static class PartHistory
{
    /// <param name="plugins">The plugin of each version of the record, lowest first; the version just below the cleaned mod's lies at <paramref name="cleanedModIndex"/> - 1.</param>
    /// <param name="cleanedModIndex">The index of the cleaned mod's version, at least 1.</param>
    /// <param name="trust">Names the patches, whose versions hold what earlier patchers wrote, and the plugins that need the cleaned mod.</param>
    /// <param name="differs">Whether the values of two versions differ, given the lower index first.</param>
    /// <remarks>
    /// A real plugin that sets the cleaned mod's value back changed the part itself, so the part is left to it. A patch of any group
    /// (this group's patch or another group's, recognised by its marker) holding the cleaned mod's value is an earlier patcher's
    /// restore to the version below its own cleaned mod, so the part is undone.
    /// A plugin that loads without the cleaned mod was made against the version below it, so one that holds the cleaned mod's value
    /// set that value itself, and the part is left to it. A plugin that needs the cleaned mod would not load without it, so its change
    /// keeps nothing: the part is undone, or left to a plugin that loads without the cleaned mod and holds the value it ends with. When
    /// such a plugin only has a version below it, its value cannot be written back, so the part stays as the plugin that needs the
    /// cleaned mod left it.
    /// </remarks>
    public static PartOutcome Of(IReadOnlyList<PluginName> plugins, int cleanedModIndex, ModTrustTable trust, Func<int, int, bool> differs)
    {
        if (!differs(cleanedModIndex - 1, cleanedModIndex)) return new PartOutcome.Unchanged();
        var lastChanger = LastChangerAbove(plugins.Count, cleanedModIndex, differs);
        var lastLoadingWithout = LastLoadingWithoutCleanedModAbove(plugins, cleanedModIndex, trust);
        if (lastChanger is not { } changer) return LeftToOrUndone(plugins, lastLoadingWithout);
        if (trust.IsAnyGroupsPatch(plugins[changer]) && !differs(cleanedModIndex, changer)) return new PartOutcome.Undo();
        if (trust.NeedsCleanedMod(plugins[changer]) && !(lastLoadingWithout < changer)) return LeftToOrUndone(plugins, lastLoadingWithout);
        return new PartOutcome.LeftToLaterPlugin(plugins[changer]);
    }

    /// <summary>Left to the plugin of the given version, which holds the value the part ends with; undone when there is none.</summary>
    private static PartOutcome LeftToOrUndone(IReadOnlyList<PluginName> plugins, int? holder) =>
        holder is { } index ? new PartOutcome.LeftToLaterPlugin(plugins[index]) : new PartOutcome.Undo();

    /// <summary>The outcome of one part of a record whose versions were read from the one just below the cleaned mod's.</summary>
    /// <param name="plugins">The plugin of each of <paramref name="versions"/>.</param>
    /// <param name="includes">Whether a set of parts includes the part.</param>
    public static PartOutcome Of<TParts>(
        ImmutableArray<VersionDifferences<TParts>> versions, IReadOnlyList<PluginName> plugins, ModTrustTable trust, Func<TParts, bool> includes) =>
        Of(plugins, VersionComparison.NamedPluginIndex, trust, VersionComparison.Differs(versions, includes));

    /// <summary>
    /// As <see cref="Of{TParts}"/>; a part that would be undone but whose earlier value cannot be written into the winner is
    /// <see cref="PartOutcome.KeptForRecordFormat"/>.
    /// </summary>
    public static PartOutcome Of(PartDifferences part, ModTrustTable trust)
    {
        var outcome = Of(part.Versions, PluginsOf(part.Versions), trust, static differs => differs);
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

    /// <returns>The highest version above the cleaned mod's of a real plugin, no patch, that loads without the cleaned mod; null when there is none.</returns>
    private static int? LastLoadingWithoutCleanedModAbove(IReadOnlyList<PluginName> plugins, int cleanedModIndex, ModTrustTable trust)
    {
        for (var index = plugins.Count - 1; index > cleanedModIndex; index--)
        {
            if (!trust.IsAnyGroupsPatch(plugins[index]) && !trust.NeedsCleanedMod(plugins[index])) return index;
        }
        return null;
    }
}
