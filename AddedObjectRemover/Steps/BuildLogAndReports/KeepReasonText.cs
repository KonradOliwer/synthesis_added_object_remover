using System.Diagnostics;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The wording of the reasons a target object is kept.</summary>
internal static class KeepReasonText
{
    private const string TeleportDoor = "teleport door";
    private const string LinkedGroupCategory = "linked to a kept object";
    private const string PlacedCategoryPrefix = "placed object: ";
    private const string CheckFailed = "unexpected error while checking what depends on it";

    /// <summary>Groups reasons in the summary, e.g. "placed object: Enable Parent" or "Quest record".</summary>
    public static string Category(KeepReason reason) => reason.Kind switch
    {
        KeepKind.TeleportDoor when reason.Link is null => TeleportDoor,
        KeepKind.TeleportDoor or KeepKind.PlacedReference => PlacedCategoryPrefix + Relation(Link(reason).Relation),
        KeepKind.NonPlacedReference => $"{Link(reason).SourceRecordType} record",
        KeepKind.LinkedGroup => LinkedGroupCategory,
        KeepKind.CheckFailed => CheckFailed,
        _ => throw new UnreachableException($"Unknown keep kind {reason.Kind}."),
    };

    /// <summary>The full reason for the log, naming the linking record.</summary>
    public static string Detail(KeepReason reason) => reason.Kind switch
    {
        KeepKind.TeleportDoor when reason.Link is null => TeleportDoor,
        KeepKind.TeleportDoor or KeepKind.PlacedReference => $"{Relation(Link(reason).Relation)} of {Link(reason).Source}",
        KeepKind.NonPlacedReference =>
            $"linked from {Link(reason).SourceRecordType} {RecordLabels.Of(Link(reason).Source, Link(reason).SourceEditorId)}",
        KeepKind.LinkedGroup => LinkedGroupDetail(reason.Keeper!),
        KeepKind.CheckFailed => CheckFailed,
        _ => throw new UnreachableException($"Unknown keep kind {reason.Kind}."),
    };

    private static string LinkedGroupDetail(GroupKeeper keeper) =>
        $"linked to {RecordLabels.Of(keeper.Key, keeper.EditorId)}, which is kept: {Detail(keeper.Reason)}";

    private static LinkFact Link(KeepReason reason) =>
        reason.Link ?? throw new InvalidOperationException($"A {reason.Kind} reason needs the link that keeps the object.");

    private static string Relation(RelationKind relation) => relation switch
    {
        RelationKind.TeleportDestination => "teleport destination",
        RelationKind.EnableParent => "Enable Parent",
        RelationKind.LinkedReference => "Linked Reference",
        RelationKind.ActivateParent => "Activate Parent",
        RelationKind.AttachRef => "Attach Ref",
        RelationKind.Other => "other link",
        _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, "A base object link is never a reason."),
    };
}
