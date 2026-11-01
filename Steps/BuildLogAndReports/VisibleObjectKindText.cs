using System.Diagnostics;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static class VisibleObjectKindText
{
    /// <summary>The kind's name as the settings screen lists it under "Never remove these as visible objects left behind".</summary>
    public static string KindName(VisibleObjectKind kind) => kind switch
    {
        VisibleObjectKind.NpcsAndCreatures => "NPCs and creatures",
        VisibleObjectKind.Containers => "Containers",
        VisibleObjectKind.Furniture => "Furniture",
        VisibleObjectKind.Activators => "Activators",
        _ => throw new UnreachableException($"Unknown visible object kind {kind}."),
    };

    /// <summary>Empty for an object of no listed kind.</summary>
    public static string KindNameOrEmpty(VisibleObjectKind? kind) => kind is { } listed ? KindName(listed) : string.Empty;
}
