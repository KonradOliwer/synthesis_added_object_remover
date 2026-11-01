using System.Collections.Immutable;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>How the parts of a cell record differ across its versions, compared on the records as their plugins store them.</summary>
internal static class CellPartHistory
{
    /// <returns>Every part of the part table, in its order.</returns>
    public static ImmutableArray<CellPartDifferences> Of(CellRecordParts table, RecordVersions<ICellGetter> read, PluginNames names) =>
    [
        .. table.For(read.Versions).Select(part => new CellPartDifferences(
            new CellPartName(part.Key.Part, part.Key.Region is { } region ? names.KeyOf(region) : null),
            DifferencesOf(part.Compare, read))),
    ];

    private static PartDifferences DifferencesOf(RecordPart<ICellGetter, ICell> part, RecordVersions<ICellGetter> read)
    {
        var versions = read.Versions;
        return VersionComparison.ComparePart(
            read.Plugins, (lower, higher) => part.Differs(versions[lower], versions[higher]), !RawByteFormat.AllowsCopy(part, read.Earlier, read.Winner));
    }
}
