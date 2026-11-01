using System.Collections.Immutable;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>What the target plugin last changed in a LAND record of another plugin it overrides, part by part, judged on the records as their plugins store them.</summary>
internal static class LandPartHistory
{
    /// <param name="patch">The patch, whose version holds what earlier patchers of the group wrote.</param>
    /// <remarks>
    /// A LAND record that any version deletes is never written, so none of its parts goes back. No LAND part holds bytes Mutagen does
    /// not read, so the record format never keeps a part.
    /// </remarks>
    /// <param name="cellDeletedInAVersion">A version read of the cell holding the LAND record deletes the cell.</param>
    public static LandRecordFact Of(
        LandRecordParts table, RecordKey key, ExteriorCell cell, RecordVersions<ILandscapeGetter> read, PluginName patch, bool cellDeletedInAVersion)
    {
        var points = read.Versions.Select(LandPoints.Read).ToList();
        var deleted = read.Versions.Any(version => version.IsDeleted);
        var pointOutcomes = Judge(table.Points, points, read.Plugins, patch);
        var colourOutcomes = Judge(table.Colours, points, read.Plugins, patch);
        var quarterOutcomes = Judge(table.Quarters, read.Versions, read.Plugins, patch);
        var restOutcome = Judge([table.Rest], read.Versions, read.Plugins, patch)[0];

        var leftToLater = new LandLeftToLater(
            PointSet.Of(LeftIndices(pointOutcomes)),
            PointSet.Of(LeftIndices(colourOutcomes)),
            QuarterSet.Of(LeftIndices(quarterOutcomes)),
            restOutcome is PartOutcome.LeftToLaterPlugin,
            LastChangersInLoadOrder([.. pointOutcomes, .. colourOutcomes, .. quarterOutcomes, restOutcome], read.Plugins));
        var changes = new LandPartChanges(
            PointSet.Of(deleted ? [] : UndoIndices(pointOutcomes)),
            PointSet.Of(deleted ? [] : UndoIndices(colourOutcomes)),
            QuarterSet.Of(deleted ? [] : UndoIndices(quarterOutcomes)),
            !deleted && restOutcome is PartOutcome.Undo,
            leftToLater);
        return new LandRecordFact(key, cell, read.WinningMod, changes, HeightsToKeep(pointOutcomes, points[0].Heights, points[^1].Heights), cellDeletedInAVersion);
    }

    /// <returns>Null when no version changed a height or normal and both versions hold the same heights; they are then read when needed.</returns>
    private static LandHeights? HeightsToKeep(PartOutcome[] pointOutcomes, float[]? earlier, float[]? current) =>
        pointOutcomes.All(outcome => outcome is PartOutcome.Unchanged) && SameHeights(earlier, current) ? null : new LandHeights(earlier, current);

    private static bool SameHeights(float[]? earlier, float[]? current) =>
        (earlier, current) switch
        {
            (null, null) => true,
            ({ } one, { } other) => one.AsSpan().SequenceEqual(other),
            _ => false,
        };

    private static PartOutcome[] Judge<TVersion, TTarget>(
        IReadOnlyList<RecordPart<TVersion, TTarget>> parts, IReadOnlyList<TVersion> versions, IReadOnlyList<PluginName> plugins, PluginName patch) =>
        [.. parts.Select(part => PartHistory.Of(plugins, PartHistory.TargetVersionIndex, patch, (lower, higher) => part.Differs(versions[lower], versions[higher])))];

    private static IEnumerable<int> UndoIndices(PartOutcome[] outcomes) =>
        outcomes.Index().Where(entry => entry.Item is PartOutcome.Undo).Select(entry => entry.Index);

    private static IEnumerable<int> LeftIndices(PartOutcome[] outcomes) =>
        outcomes.Index().Where(entry => entry.Item is PartOutcome.LeftToLaterPlugin).Select(entry => entry.Index);

    private static ImmutableArray<PluginName> LastChangersInLoadOrder(IEnumerable<PartOutcome> outcomes, IReadOnlyList<PluginName> plugins)
    {
        var lastChangers = outcomes.OfType<PartOutcome.LeftToLaterPlugin>().Select(left => left.Plugin).ToHashSet();
        return [.. plugins.Where(lastChangers.Contains).Distinct()];
    }
}
