using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static class ObjectVisibilityText
{
    /// <summary>Why the object is invisible, e.g. "Lights without a model" or "base not found"; "visible" otherwise.</summary>
    public static string Describe(ObjectVisibility visibility)
    {
        if (visibility.BaseMissing) return "base not found";
        if (visibility.EffectOnlyMesh) return "effect-only mesh";
        return visibility.Kind is { } kind ? KindName(kind) : "visible";
    }

    /// <summary>The kind's name as the settings screen lists it under "Never remove these as left behind".</summary>
    public static string KindName(InvisibleObjectKind kind) => kind switch
    {
        InvisibleObjectKind.MapMarkers => "Map markers",
        InvisibleObjectKind.XMarkers => "Position markers (XMarker, XMarkerHeading)",
        InvisibleObjectKind.IdleMarkers => "Idle animation spots",
        InvisibleObjectKind.FurnitureMarkers => "Furniture use spots",
        InvisibleObjectKind.DoorMarkers => "Invisible marker doors",
        InvisibleObjectKind.OtherMarkers => "Other invisible objects",
        InvisibleObjectKind.Lights => "Lights without a model",
        InvisibleObjectKind.SoundMarkers => "Sound sources",
        InvisibleObjectKind.AcousticSpaces => "Echo areas",
        InvisibleObjectKind.CritterSpawners => "Critter spawners",
        InvisibleObjectKind.TriggerBoxes => "Invisible trigger areas",
        InvisibleObjectKind.Decals => "Surface decals (dirt, blood)",
        _ => throw new UnreachableException($"Unknown invisible object kind {kind}."),
    };
}
