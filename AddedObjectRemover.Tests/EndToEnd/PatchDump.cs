using System.Globalization;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The placed-record overrides of a patch, one line each, in FormKey order.</summary>
internal static class PatchDump
{
    private const string CoordinateFormat = "F2";
    private const string None = "none";

    public static IReadOnlyList<string> Describe(ISkyrimModGetter patch) =>
        patch.EnumerateMajorRecords<ICellGetter>()
            .SelectMany(cell => cell.EnumeratePlaced().Select(entry => Describe(cell, entry.Record, entry.Persistent)))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string Describe(ICellGetter cell, IPlacedGetter record, bool persistent) =>
        $"{record.FormKey} in {cell.FormKey} ({(persistent ? "persistent" : "temporary")}): "
        + $"disabled {record.IsInitiallyDisabled()}, {DescribePlacement(record.Placement)}, enable parent {DescribeEnableParent(record.EnableParent)}";

    private static string DescribePlacement(IPlacementGetter? placement) =>
        placement == null
            ? $"placement {None}"
            : $"position ({Format(placement.Position.X)}, {Format(placement.Position.Y)}, {Format(placement.Position.Z)}), "
              + $"rotation ({Format(placement.Rotation.X)}, {Format(placement.Rotation.Y)}, {Format(placement.Rotation.Z)})";

    private static string DescribeEnableParent(IEnableParentGetter? enableParent) =>
        enableParent == null ? None : $"{enableParent.Reference.FormKey} {enableParent.Flags}";

    private static string Format(float value) => value.ToString(CoordinateFormat, CultureInfo.InvariantCulture);
}
