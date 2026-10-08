using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>Text pieces several log sections and report files share.</summary>
internal static class Describe
{
    public const string ListSeparator = ", ";

    /// <summary>The text for a list with no entries.</summary>
    public const string NoEntries = "none";

    public static string OtherObject(OtherObject other) =>
        $"{RecordLabels.Of(other.Key, other.EditorId)} {OriginLabel(other.Key, other.WinningMod)}";

    /// <summary>"from Origin.esp", plus the winning plugin when a later plugin overrides the record.</summary>
    public static string OriginLabel(RecordKey key, PluginName winningMod) =>
        key.Plugin == winningMod
            ? $"from {key.Plugin}"
            : $"from {key.Plugin} (winning override in {winningMod})";

    public static string Space(SpaceFact space)
    {
        var label = RecordLabels.Of(space.Key, space.EditorId);
        return space.Kind switch
        {
            SpaceKind.Worldspace => $"worldspace {label}",
            SpaceKind.Interior => $"interior {label}",
            _ => throw new UnreachableException($"Unknown space kind {space.Kind}."),
        };
    }

    public static string Space(CollectedObjects world, RecordKey spaceKey) => Space(world.Spaces[spaceKey]);

    public static string Cell(CellFact cell) => RecordLabels.Of(cell.Key, cell.EditorId);

    /// <summary>Empty for an interior, whose cell is the space itself.</summary>
    public static string CellOrEmpty(TargetObject target) => target.Cell is { } cell ? Cell(cell) : string.Empty;

    public static string Location(CollectedObjects world, TargetObject target)
    {
        var space = Space(world, target.SpaceKey);
        return target.Cell is { } cell ? $"{space}, cell {Cell(cell)}" : space;
    }

    public static string PointReason(PointReason reason) => reason.Kind switch
    {
        PointReasonKind.RaceNotFound => $"race {reason.Race} not found",
        PointReasonKind.NoBodyMeshNoBoundsNotPlayable =>
            $"no body mesh for race {RecordLabels.Of(reason.Race, reason.RaceEditorId)}, no Object Bounds, race not playable",
        _ => throw new UnreachableException($"Unknown point reason {reason.Kind}."),
    };

    public static string RemovalReason(CollectedObjects world, RemovedObject removal) => removal switch
    {
        TooCloseRemoval { TooCloseTo: var other } => $"too close to {OtherObject(other)}",
        TouchingRemoval touching => $"touches removed {RecordNames.Describe(world.Targets[touching.TouchedTargetIndex])}",
        AnchoringRemoval anchoring =>
            $"{TextFormat.Percent(anchoring.RemovedShare)} of its support was removed (mostly {RecordNames.Describe(world.Targets[anchoring.MainRemovedSupporter])})",
        LeftBehindRemoval { Evaluation: var evaluation } => $"invisible, {LeftBehindReason(evaluation)}",
        LinkedRemoval linked => $"linked to removed {RecordNames.Describe(world.Targets[linked.LinkedToTargetIndex])}",
        _ => throw new UnreachableException($"Unknown removal type {removal.GetType().Name}."),
    };

    /// <summary>The reason, naming the other mod's object the invisible object sits inside, if any.</summary>
    public static string LeftBehindReason(LeftBehindCheck evaluation) =>
        evaluation.ContainingObject is { } inside
            ? $"{LeftBehindOutcomeText.DescribeReason(evaluation)} (inside {OtherObject(inside)})"
            : LeftBehindOutcomeText.DescribeReason(evaluation);

    public static string HintType(ManualPatchHintType type) => type switch
    {
        ManualPatchHintType.RemovedMarker => "Removed marker",
        ManualPatchHintType.KeptLinkedGroup => "Kept linked group of",
        ManualPatchHintType.KeptForNonPlacedReference => "Kept, referenced by a non-placed record:",
        ManualPatchHintType.KeptTeleportDoor => "Kept teleport door",
        _ => throw new UnreachableException($"Unknown manual patch hint type {type}."),
    };

    /// <summary>" in 1.2s" with the detailed log; nothing otherwise, since timings are detail.</summary>
    public static string TimedSuffix(TimeSpan elapsed, ReportContext context) => context.Detailed ? TextFormat.InSeconds(elapsed) : string.Empty;
}
