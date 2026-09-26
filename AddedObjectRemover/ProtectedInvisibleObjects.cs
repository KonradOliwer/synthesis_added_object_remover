using System.Diagnostics;

namespace AddedObjectRemover;

/// <summary>The invisible object kinds each <see cref="ProtectedInvisibleObjectsPreset"/> keeps.</summary>
internal static class ProtectedInvisibleObjects
{
    private static readonly InvisibleObjectKind[] Markers =
    [
        InvisibleObjectKind.MapMarkers,
        InvisibleObjectKind.XMarkers,
        InvisibleObjectKind.IdleMarkers,
        InvisibleObjectKind.FurnitureMarkers,
        InvisibleObjectKind.DoorMarkers,
    ];

    private static readonly InvisibleObjectKind[] MarkersAndLights = [.. Markers, InvisibleObjectKind.Lights];

    private static readonly InvisibleObjectKind[] MarkersLightsAndSounds =
        [.. MarkersAndLights, InvisibleObjectKind.SoundMarkers, InvisibleObjectKind.AcousticSpaces];

    public static HashSet<InvisibleObjectKind> Resolve(ProtectedInvisibleObjectsPreset preset, IEnumerable<InvisibleObjectKind> custom) => preset switch
    {
        ProtectedInvisibleObjectsPreset.None => [],
        ProtectedInvisibleObjectsPreset.Markers => [.. Markers],
        ProtectedInvisibleObjectsPreset.MarkersAndLights => [.. MarkersAndLights],
        ProtectedInvisibleObjectsPreset.MarkersLightsAndSounds => [.. MarkersLightsAndSounds],
        ProtectedInvisibleObjectsPreset.Custom => [.. custom],
        _ => throw new UnreachableException($"Unknown protected invisible objects preset {preset}."),
    };
}
