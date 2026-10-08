using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Scenes about invisible target objects left behind, and a kept marker that has to leave an other-mod building.</summary>
internal static class FixtureLeftBehindScenes
{
    private const float MarkerHeight = 100f;
    private const float SecondMarkerShift = 60f;

    /// <summary>Distance from the invisible object to the crates around it in the four main directions.</summary>
    private const float RingRadius = 400f;

    /// <summary>The kept crate's distance from the invisible object along each axis: the ring radius over the square root of two.</summary>
    private const float DiagonalRingDistance = 283f;

    private static readonly Vector3 AboveGround = new(0f, 0f, MarkerHeight);

    private static readonly Vector3[] RemovedRingOffsets =
    [
        new(RingRadius, 0f, 0f),
        new(0f, RingRadius, 0f),
        new(-RingRadius, 0f, 0f),
        new(0f, -RingRadius, 0f),
    ];

    public static void AddAll(FixtureScenery scenery)
    {
        AddInsideBuildingScene(scenery);
        AddClearedSurroundingsScene(scenery, FixtureSlot.LeftoverLight, "Light", scenery.Bases.TargetLight);
        AddClearedSurroundingsScene(scenery, FixtureSlot.LeftoverSound, "Sound", scenery.Bases.TargetSoundMarker);
        AddRelocatedMarkerScene(scenery);
    }

    /// <summary>Two invisible objects inside an other-mod building: removed as inside it unless their kind is protected.</summary>
    private static void AddInsideBuildingScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.LeftoverInside);
        scenery.Mods.OtherMod.Interior.Place("R_InsideBuilding", scenery.Bases.OtherModBuilding, origin);
        scenery.Mods.Target.Interior.Place("T_MarkerInsideBuilding", scenery.Bases.TargetMarker, origin + AboveGround);
        scenery.Mods.Target.Interior.Place(
            "T_SoundInsideBuilding", scenery.Bases.TargetSoundMarker, origin + AboveGround + new Vector3(SecondMarkerShift, 0f, 0f));
    }

    /// <summary>
    /// An invisible object with four crates around it that are removed and one farther crate that
    /// stays: the surroundings are cleared, so it goes unless its kind is protected.
    /// </summary>
    private static void AddClearedSurroundingsScene(FixtureScenery scenery, FixtureSlot slot, string name, FormKey invisibleBase)
    {
        var origin = FixtureLayout.Origin(slot);
        scenery.Mods.Target.Interior.Place($"T_{name}", invisibleBase, origin + AboveGround);
        for (var direction = 0; direction < RemovedRingOffsets.Length; direction++)
        {
            scenery.PlaceCratePinnedByPad($"{name}Ring{direction}", origin + RemovedRingOffsets[direction]);
        }
        scenery.PlaceTargetCrate($"{name}RingKept", origin + new Vector3(DiagonalRingDistance, DiagonalRingDistance, 0f));
    }

    /// <summary>
    /// A marker inside an other-mod building on the terrain that stays because a form list references it:
    /// it is moved out of the building when kept markers are moved. A crate beside a pad shares the cell.
    /// </summary>
    private static void AddRelocatedMarkerScene(FixtureScenery scenery)
    {
        var ground = new Vector3(FixtureExteriorLayout.BuildingX, FixtureExteriorLayout.BuildingY, FixtureSpaces.TerrainHeight);
        scenery.Mods.OtherMod.Exterior.Place("R_ExteriorBuilding", scenery.Bases.OtherModBuilding, ground);
        var marker = scenery.Mods.Target.Exterior.Place("T_ExteriorMarkerInBuilding", scenery.Bases.TargetMarker, ground + AboveGround);
        scenery.Mods.ReferenceFromKeepList(marker);

        var cratePosition = new Vector3(FixtureExteriorLayout.CrateX, FixtureExteriorLayout.CrateY, FixtureSpaces.TerrainHeight);
        scenery.Mods.OtherMod.Exterior.Place("R_ExteriorCrate_Pad", scenery.Bases.OtherModPad, cratePosition + FixtureLayout.PinningPadOffset);
        scenery.Mods.Target.Exterior.Place("T_ExteriorCrate", scenery.Bases.TargetCrate, cratePosition);
    }
}

/// <summary>Where the exterior scene stands in the single exterior cell (cell 0, 0 spans 0 to 4096).</summary>
internal static class FixtureExteriorLayout
{
    public const float BuildingX = 2048f;
    public const float BuildingY = 2048f;
    public const float CrateX = 600f;
    public const float CrateY = 600f;
}
