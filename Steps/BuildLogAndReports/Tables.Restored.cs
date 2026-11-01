using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    private const string OldPlaceNotChecked = "not checked";
    private const string OldPlaceFree = "free";

    /// <remarks>A property, not a field: the field initializers of the parts of this class run in no defined order, and this one reads <see cref="RemovalsHeader"/>.</remarks>
    private static ImmutableArray<string> RestoredHeader =>
    [
        .. RemovalsHeader,
        "earlierPlugin", "newX", "newY", "newZ", "newRotZDeg", "newScale", "newBase", "oldPlaceCheck", "leftToLaterPlugins",
    ];

    /// <summary>One row per restored object, in the order of the removals table: where it stood, why it was taken from there, and the version it went back to.</summary>
    /// <remarks>The new base is filled only for an object whose model went back; it is empty when the earlier version has the same base object.</remarks>
    /// <returns>Null when nothing was restored.</returns>
    public static CsvTable? BuildRestoredTable(CollectedObjects world, IEnumerable<ObjectWithReason> restorations, RunOutcome outcome, ReportContext context)
    {
        ImmutableArray<ImmutableArray<string>> rows = [.. InAreaOrder(world, restorations).Select(restoration => RestoredRow(world, restoration, outcome, context))];
        return rows.IsEmpty ? null : new CsvTable(ReportFileNames.RestoredFileName, RestoredHeader, rows);
    }

    /// <remarks>The new placement is the one the patch writes: each of position, rotation and scale from the earlier version only when it goes back.</remarks>
    private static ImmutableArray<string> RestoredRow(CollectedObjects world, ObjectWithReason restoration, RunOutcome outcome, ReportContext context)
    {
        var restored = (Decision.Restored)outcome.Final.Of(new TargetId(restoration.TargetIndex))!;
        var earlier = restored.EarlierVersion;
        var target = world.Targets[restoration.TargetIndex];
        return CsvRow.Of(
        [
            .. RemovedObjectColumns(world, restoration, outcome.Final, context),
            Quote(earlier.Plugin.ToString()),
            .. PlacementColumns(target.RestoredAs(earlier).Placement),
            NewBaseColumn(target, earlier, context),
            Quote(outcome.ChangedObjects.OldPlaceChecks.Any(check => check.Target.Index == restoration.TargetIndex) ? OldPlaceFree : OldPlaceNotChecked),
            OptionalText(LeftToLaterPlugins(earlier)),
        ]);
    }

    /// <summary>
    /// E.g. "rotation, scale (Later.esp)" or "position (Later.esp); rotation (Other.esp)": the parts that stay as a later plugin or an
    /// earlier patcher changed them, each with the last plugin that did; null when none.
    /// </summary>
    internal static string? LeftToLaterPlugins(EarlierVersion earlier) =>
        earlier.LeftToLaterPlugins.IsEmpty
            ? null
            : Describe.PartsByPlugin(earlier.LeftToLaterPlugins.Select(left => (PartName(left.Key), left.Value)));

    /// <summary>The names of the parts of a placed record that are set, in the order of the parts.</summary>
    internal static string PartNames(PlacedParts parts) =>
        string.Join(Describe.ListSeparator, Enum.GetValues<PlacedParts>().Where(part => part != PlacedParts.None && parts.HasFlag(part)).Select(PartName));

    private static string PartName(PlacedParts part) => part switch
    {
        PlacedParts.Position => "position",
        PlacedParts.Rotation => "rotation",
        PlacedParts.Scale => "scale",
        PlacedParts.Presence => "enable state",
        PlacedParts.NavDoorLink => "navmesh door link",
        PlacedParts.BaseAndData => "base and data",
        _ => throw new UnreachableException($"Unknown placed part {part}."),
    };
}
