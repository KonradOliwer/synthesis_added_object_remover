using System.Diagnostics;

namespace AddedObjectRemover;

/// <summary>Text pieces several log sections and report files share.</summary>
internal static class Describe
{
    public static string OtherObject(OtherObject other) =>
        $"{RecordNames.Describe(other.FormKey, other.EditorId)} {RecordNames.DescribeOrigin(other.FormKey, other.WinningMod)}";

    public static string Location(World world, TargetObject target)
    {
        var space = world.SpaceNames[target.SpaceKey];
        return target.CellName == null ? space : $"{space}, cell {target.CellName}";
    }

    public static string RemovalReason(World world, Removal removal) => removal switch
    {
        TooCloseRemoval { TooCloseTo: var other } => $"too close to {OtherObject(other)}",
        TouchingRemoval touching => $"touches removed {RecordNames.Describe(world.Targets[touching.TouchedTargetIndex])}",
        AnchoringRemoval anchoring =>
            $"{anchoring.RemovedShare:P0} of its support was removed (mostly {RecordNames.Describe(world.Targets[anchoring.MainRemovedSupporter])})",
        LeftoverRemoval { Evaluation: var evaluation } => $"invisible, {LeftoverReason(evaluation)}",
        LinkedRemoval linked => $"linked to removed {RecordNames.Describe(world.Targets[linked.LinkedToTargetIndex])}",
        _ => throw new UnreachableException($"Unknown removal type {removal.GetType().Name}."),
    };

    /// <summary>The reason, naming the other mod's object the invisible object sits inside, if any.</summary>
    public static string LeftoverReason(LeftoverEvaluation evaluation) =>
        evaluation.ContainingObject is { } inside
            ? $"{evaluation.DescribeReason()} (inside {OtherObject(inside)})"
            : evaluation.DescribeReason();

    public static string HintType(ManualPatchHintType type) => type switch
    {
        ManualPatchHintType.RemovedMarker => "Removed marker",
        ManualPatchHintType.KeptLinkedGroup => "Kept linked group of",
        ManualPatchHintType.KeptForNonPlacedReference => "Kept, referenced by a non-placed record:",
        ManualPatchHintType.KeptTeleportDoor => "Kept teleport door",
        _ => throw new UnreachableException($"Unknown manual patch hint type {type}."),
    };

    public static string Seconds(TimeSpan elapsed) => $"{elapsed.TotalSeconds:F1}s";

    /// <summary>" in 1.2s" with the detailed log; nothing otherwise, since timings are detail.</summary>
    public static string TimedSuffix(TimeSpan elapsed, ReportContext context) => context.Detailed ? $" in {Seconds(elapsed)}" : string.Empty;
}
