using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The wording of what became of other mods' objects left without support, shared by the log and the report file.</summary>
internal static class OtherModsObjectText
{
    private static readonly string NothingBelow = $"nothing below within {OtherModsObjectsLimits.MaxPullDistance} units";

    /// <summary>The fate of several objects, as the summary counts them.</summary>
    public static string Summary(OtherModsObjectFate fate) => fate switch
    {
        OtherModsObjectFate.PulledDown => "pulled down",
        OtherModsObjectFate.Removed => "removed",
        OtherModsObjectFate.RemovedNothingBelow => $"removed, {NothingBelow}",
        OtherModsObjectFate.LeftStillTouching => "left where they stand, still touching what is below them",
        OtherModsObjectFate.LeftNotRemovable => "left where they stand, not removable",
        OtherModsObjectFate.LeftNothingBelowNotRemovable => $"left where they stand, {NothingBelow} and not removable",
        OtherModsObjectFate.LeftWonAfterPatch => "left as they are, a plugin after the patch wins them",
        _ => throw new UnreachableException($"Unknown fate {fate}."),
    };

    /// <summary>The fate of one object.</summary>
    public static string Outcome(OtherModsObjectFate fate) => fate switch
    {
        OtherModsObjectFate.PulledDown => "pulled down",
        OtherModsObjectFate.Removed => "removed",
        OtherModsObjectFate.RemovedNothingBelow => $"removed, {NothingBelow}",
        OtherModsObjectFate.LeftStillTouching => "left where it stands, still touching what is below it",
        OtherModsObjectFate.LeftNotRemovable => "left where it stands, not removable",
        OtherModsObjectFate.LeftNothingBelowNotRemovable => $"left where it stands, {NothingBelow} and not removable",
        OtherModsObjectFate.LeftWonAfterPatch => "left as it is, a plugin after the patch wins it",
        _ => throw new UnreachableException($"Unknown fate {fate}."),
    };

    /// <summary>Each role in the order of <see cref="ReferenceRoles"/>, e.g. "persistent, has an enable parent"; empty for none.</summary>
    public static string Roles(ReferenceRoles roles) =>
        string.Join(Describe.ListSeparator, Enum.GetValues<ReferenceRoles>().Where(role => role != ReferenceRoles.None && roles.HasFlag(role)).Select(RoleName));

    private static string RoleName(ReferenceRoles role) => role switch
    {
        ReferenceRoles.Persistent => "persistent",
        ReferenceRoles.TeleportDoor => "teleport door",
        ReferenceRoles.HasEnableParent => "has an enable parent",
        ReferenceRoles.ScriptNamesQuestOrScene => "its script names a quest or scene",
        ReferenceRoles.LocationRefType => "has a location ref type",
        ReferenceRoles.LinkedFromPlacedRecord => "another placed object links to it",
        ReferenceRoles.MapMarker => "map marker",
        _ => throw new UnreachableException($"Unknown reference role {role}."),
    };
}
