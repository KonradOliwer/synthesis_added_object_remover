using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>Text pieces several log sections and report files share.</summary>
internal static class Describe
{
    public const string ListSeparator = ", ";

    /// <summary>The text for a list with no entries.</summary>
    public const string NoEntries = "none";

    private const string RemovedVerb = "removed";

    private const string RestoredVerb = "restored";

    /// <summary>Named for a change only the patch holds, which this run did not make.</summary>
    private const string EarlierPatcher = "an earlier patcher in this group";

    private static readonly ImmutableArray<(TerrainChangeKind Kind, string Text)> TerrainChangeTexts =
    [
        (TerrainChangeKind.Height, "height"),
        (TerrainChangeKind.TerrainType, "ground type"),
        (TerrainChangeKind.WaterHeight, "water height"),
        (TerrainChangeKind.WaterAdded, "water added"),
        (TerrainChangeKind.WaterRemoved, "water removed"),
    ];

    public static string OtherObject(OtherObject other) =>
        LabelWithOverride(other.Key, other.EditorId, other.WinningMod);

    /// <summary>The record label; the key already names the origin plugin, so only a later overriding plugin is added.</summary>
    public static string LabelWithOverride(RecordKey key, string? editorId, PluginName winningMod)
    {
        var label = RecordLabels.Of(key, editorId);
        return key.Plugin == winningMod ? label : $"{label} (winning override in {winningMod})";
    }

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

    /// <summary>E.g. "its cell Cell [...] is deleted by CityMod.esp".</summary>
    public static string DeletedCell(DeletedCell deleted) => $"its cell {Cell(deleted.Cell)} is deleted by {deleted.DeletedBy}";

    /// <summary>Empty for an interior, whose cell is the space itself.</summary>
    public static string CellOrEmpty(TargetObject target) => target.Cell is { } cell ? Cell(cell) : string.Empty;

    public static string Location(CollectedObjects world, TargetObject target)
    {
        var space = Space(world, target.SpaceKey);
        return target.Cell is { } cell ? $"{space}, cell {Cell(cell)}" : space;
    }

    public static string PointReason(PointReason reason) => reason.Kind switch
    {
        PointReasonKind.RaceNotFound when reason.Race == RecordKey.Null || reason.Race == RecordKey.None => "has no race set",
        PointReasonKind.RaceNotFound => $"race {reason.Race} not found",
        PointReasonKind.NoBodyMeshNoBoundsNotPlayable =>
            $"no body mesh for race {RecordLabels.Of(reason.Race, reason.RaceEditorId)}, no Object Bounds, race not playable",
        _ => throw new UnreachableException($"Unknown point reason {reason.Kind}."),
    };

    /// <summary>Why the object left its place, and, for another mod's object disabled although its mode restores it, why it was not restored.</summary>
    /// <param name="final">The decisions, which tell a target object the reason names as taken from its place, restored or removed.</param>
    public static string LeftItsPlaceReason(CollectedObjects world, ObjectWithReason removal, IRemovalDecisions final) =>
        RemovalReason(world, removal, final) + NotRestoredReason(world, removal.TargetIndex, final);

    /// <summary>Between the parts left to one plugin and those left to the next.</summary>
    private const string PluginGroupSeparator = "; ";

    /// <summary>
    /// E.g. "rotation, scale (Later.esp); position (Other.esp)": the parts of a record that a later plugin changed again, each group
    /// with the last plugin that did, in the order of each plugin's first part.
    /// </summary>
    public static string PartsByPlugin(IEnumerable<(string Part, PluginName Plugin)> parts) =>
        string.Join(
            PluginGroupSeparator,
            parts.GroupBy(left => left.Plugin).Select(byPlugin => $"{string.Join(ListSeparator, byPlugin.Select(left => left.Part))} ({byPlugin.Key})"));

    /// <summary>"; not restored: changed again by Later.esp" for another mod's object disabled although its mode restores it; nothing for any other object.</summary>
    private static string NotRestoredReason(CollectedObjects world, int targetIndex, IRemovalDecisions final) =>
        final.WhenRemovedOf(new TargetId(targetIndex)) switch
        {
            WhenRemoved.DisableInsteadOfRestore { CellDeleted: { } deleted } => $"; not restored: the cell it would go back into, {Cell(deleted.Cell)}, is deleted by {deleted.DeletedBy}",
            WhenRemoved.DisableInsteadOfRestore { ChangedLaterBy: { } later } => $"; not restored: changed again by {later}",
            WhenRemoved.DisableInsteadOfRestore => $"; not restored: its {ShownChange(world.Targets[targetIndex].Origin)} does not go back",
            _ => string.Empty,
        };

    private static string ShownChange(TargetOrigin origin) => origin switch
    {
        TargetOrigin.Moved => "position",
        TargetOrigin.Replaced => "base object",
        _ => throw new UnreachableException($"Unknown changed-object origin {origin}."),
    };

    /// <param name="final">The decisions, which tell a target object the reason names as taken from its place, restored or removed.</param>
    public static string RemovalReason(CollectedObjects world, ObjectWithReason removal, IRemovalDecisions final) => removal switch
    {
        ObjectWithReason.TerrainChanged or ObjectWithReason.TooClose => CollisionReason(removal),
        ObjectWithReason.Touching touching => $"touches {FateOf(final, touching.TouchedTargetIndex)} {RecordNames.Describe(world.Targets[touching.TouchedTargetIndex])}",
        ObjectWithReason.LostSupport support => LostSupportReason(world, support, final),
        ObjectWithReason.TouchingAnotherModsObject touching => $"touches {OtherObject(touching.Touched)}, another mod's object left without support",
        ObjectWithReason.LostSupportWithAnotherModsObject support =>
            $"{TextFormat.Percent(support.LeftItsPlaceShare)} of its support {(support.RestoredShare > 0 ? "left its place or was pulled down" : "was removed or pulled down")} "
            + $"(mostly {OtherObject(support.MainSupporter)}, another mod's object left without support)",
        ObjectWithReason.VisibleLeftBehind { Evaluation: var evaluation } => $"visible, {LeftBehindOutcomeText.DescribeReason(evaluation)}",
        ObjectWithReason.InClearedArea { Area: var area } => ClearedAreaText.Reason(area),
        ObjectWithReason.InGroupWithClash inGroup =>
            $"in a group with a clash removal (seed {FateOf(final, inGroup.SeedTargetIndex)} {RecordNames.Describe(world.Targets[inGroup.SeedTargetIndex])}, group of {TextFormat.Count(inGroup.GroupSize)})",
        ObjectWithReason.LeftBehind { Evaluation: var evaluation } => $"invisible, {LeftBehindReason(evaluation)}",
        ObjectWithReason.LinkedTo linked => $"linked to {FateOf(final, linked.LinkedToTargetIndex)} {RecordNames.Describe(world.Targets[linked.LinkedToTargetIndex])}",
        _ => throw new UnreachableException($"Unknown removal type {removal.GetType().Name}."),
    };

    /// <summary>
    /// "100 % of its support was removed (mostly X)", or, when restored objects held part of it, "100 % of its support left its place
    /// (mostly X, restored)" with what became of the main supporter at the end.
    /// </summary>
    private static string LostSupportReason(CollectedObjects world, ObjectWithReason.LostSupport support, IRemovalDecisions final)
    {
        var share = TextFormat.Percent(support.LeftItsPlaceShare);
        var main = RecordNames.Describe(world.Targets[support.MainSupporter]);
        return support.RestoredShare > 0
            ? $"{share} of its support left its place (mostly {main}, {FateOf(final, support.MainSupporter)})"
            : $"{share} of its support was removed (mostly {main})";
    }

    /// <summary>The reason of an object that clashes with a change of the terrain or with another mod's object.</summary>
    public static string CollisionReason(ObjectWithReason collision) => collision switch
    {
        ObjectWithReason.TerrainChanged changed => $"terrain changed by {ChangingMod(changed.ChangedBy)} ({TerrainChange(changed.Kind)})",
        ObjectWithReason.TooClose { TooCloseTo: var other, Cause: TooCloseCause.ObjectSize } => $"too close to {OtherObject(other)}, {Change(other)}",
        ObjectWithReason.TooClose { TooCloseTo: var other, Cause: TooCloseCause.Distance distance } => $"within {distance.Units} units of {OtherObject(other)}, {Change(other)}",
        _ => throw new UnreachableException($"Unexpected collision type {collision.GetType().Name}."),
    };

    /// <summary>"restored" for a target object that was restored, "removed" for any other that was taken from its place.</summary>
    private static string FateOf(IRemovalDecisions final, int targetIndex) => Fate(final.Of(new TargetId(targetIndex)));

    /// <summary>"restored" for a target object that was restored, "removed" for any other that was taken from its place.</summary>
    public static string Fate(Decision? decision) => decision is Decision.Restored ? RestoredVerb : RemovedVerb;

    /// <param name="changedBy">Null when only the patch holds the change.</param>
    public static string ChangingMod(PluginName? changedBy) => changedBy?.ToString() ?? EarlierPatcher;

    /// <summary>E.g. "changed by CityMod.esp (accepted value from SomeMod.esp)" or "changed by CityMod.esp (new object)".</summary>
    private static string Change(OtherObject other)
    {
        var change = other.Change ?? throw new UnreachableException($"The other-mod object {other.Key} causes a removal but has no change.");
        var accepted = change.AcceptedFrom is { } from ? $"accepted value from {from}" : "new object";
        return $"changed by {ChangingMod(change.ChangingMod)} ({accepted})";
    }

    /// <summary>E.g. "height", "height and terrain type" or "height, terrain type and water height".</summary>
    private static string TerrainChange(TerrainChangeKind kind)
    {
        List<string> parts = [.. TerrainChangeTexts.Where(text => kind.HasFlag(text.Kind)).Select(text => text.Text)];
        return parts switch
        {
            [] => throw new UnreachableException($"A terrain change without a known kind: {kind}."),
            [var single] => single,
            [.. var first, var last] => $"{string.Join(ListSeparator, first)} and {last}",
        };
    }

    /// <summary>The reason, naming the other mod's object the invisible object sits inside, if any.</summary>
    public static string LeftBehindReason(InvisibleLeftBehindCheck evaluation) =>
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
    public static string TimedSuffix(TimeSpan elapsed, ReportContext context) => context.Detailed ? $" in {TextFormat.Seconds(elapsed)}" : string.Empty;

    /// <summary>Where a mesh's origin sits, as the log line and the mesh-origins file word it.</summary>
    public static string OriginPlace(MeshOriginPlace place) => place switch
    {
        MeshOriginPlace.NearBottom => "origin near bottom",
        MeshOriginPlace.NearCentre => "origin near centre",
        MeshOriginPlace.Other => "other",
        _ => throw new UnreachableException($"Unknown mesh origin place {place}."),
    };
}
