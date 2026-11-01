using System.Collections.Immutable;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>What the target plugin last changed in a cell record of another plugin it overrides, part by part, judged on the records as their plugins store them.</summary>
internal static class CellPartHistory
{
    /// <param name="patch">The patch, whose version holds what earlier patchers of the group wrote.</param>
    /// <returns>Every part that is not <see cref="PartOutcome.Unchanged"/>, in the order of the part table.</returns>
    /// <remarks>
    /// A cell that any version deletes is never written, so none of its parts goes back. A part whose earlier bytes Mutagen does not
    /// read cannot be written into a winner of another record format, so it stays.
    /// </remarks>
    public static ImmutableArray<PartChange> Of(CellRecordParts table, RecordVersions<ICellGetter> read, PluginName patch, PluginNames names)
    {
        var versions = read.Versions;
        var deleted = versions.Any(version => version.IsDeleted);
        return
        [
            .. table.For(versions)
                .Select(part => new PartChange(
                    new CellPartName(part.Key.Part, part.Key.Region is { } region ? names.KeyOf(region) : null),
                    OutcomeOf(part.Compare, read, patch)))
                .Where(change => change.Outcome is PartOutcome.LeftToLaterPlugin || (!deleted && change.Outcome is not PartOutcome.Unchanged)),
        ];
    }

    private static PartOutcome OutcomeOf(RecordPart<ICellGetter, ICell> part, RecordVersions<ICellGetter> read, PluginName patch)
    {
        var versions = read.Versions;
        var outcome = PartHistory.Of(read.Plugins, PartHistory.TargetVersionIndex, patch, (lower, higher) => part.Differs(versions[lower], versions[higher]));
        return outcome is PartOutcome.Undo && !RawByteFormat.AllowsCopy(part, read.Earlier, read.Winner) ? new PartOutcome.KeptForRecordFormat() : outcome;
    }
}
