using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The wording of what became of other mods' objects left without support, shared by the log and the report file.</summary>
internal static class OtherModsObjectText
{
    private static readonly string NothingBelow = $"nothing below within {OtherModsObjectsLimits.MaxPullDistance} units";
    private static readonly GrammaticalNumber One = new("it stands", "it is", "it");
    private static readonly GrammaticalNumber Several = new("they stand", "they are", "them");

    /// <summary>The fate of several objects, as the summary counts them.</summary>
    public static string Summary(OtherModsObjectFate fate) => Fate(fate, Several);

    /// <summary>The fate of one object.</summary>
    public static string Outcome(OtherModsObjectFate fate) => Fate(fate, One);

    private static string Fate(OtherModsObjectFate fate, GrammaticalNumber number) => fate switch
    {
        OtherModsObjectFate.PulledDown => "pulled down",
        OtherModsObjectFate.Removed => "removed",
        OtherModsObjectFate.RemovedNothingBelow => $"removed, {NothingBelow}",
        OtherModsObjectFate.LeftStillTouching => $"left where {number.Stand}, still touching what is below {number.Them}",
        OtherModsObjectFate.LeftNotRemovable => $"left where {number.Stand}, not removable",
        OtherModsObjectFate.LeftNothingBelowNotRemovable => $"left where {number.Stand}, {NothingBelow} and not removable",
        OtherModsObjectFate.LeftWonAfterPatch => $"left as {number.Are}, a plugin after the patch wins {number.Them}",
        OtherModsObjectFate.LeftInDeletedCell => $"left as {number.Are}, in a deleted cell",
        _ => throw new UnreachableException($"Unknown fate {fate}."),
    };

    /// <summary>The words of a fate that differ between one object and several.</summary>
    private readonly record struct GrammaticalNumber(string Stand, string Are, string Them);

    /// <summary>Each role in the order of <see cref="ReferenceRoles"/>, e.g. "persistent, has an enable parent"; empty for none.</summary>
    public static string Roles(ReferenceRoles roles) => TextLists.JoinSetFlags(roles, RoleName, Describe.ListSeparator);

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
