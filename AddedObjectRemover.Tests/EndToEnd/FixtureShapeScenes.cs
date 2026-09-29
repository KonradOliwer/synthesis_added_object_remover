using System.Numerics;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Scenes about which object is too close to which, and how the zone shape and mesh decide it.</summary>
internal static class FixtureShapeScenes
{
    /// <summary>A rival crate this close to a target crate replaces it (position tolerance 16).</summary>
    private static readonly Vector3 ReplacementShift = new(10f, 0f, 0f);

    public static void AddAll(FixtureScenery scenery)
    {
        AddTooCloseScene(scenery);
        AddTwinPillarsScene(scenery);
        AddBoundsOnlyScene(scenery);
        AddReplacementScene(scenery);
    }

    /// <summary>Removed in every variant: a rival pad reaches into the crate's zone.</summary>
    private static void AddTooCloseScene(FixtureScenery scenery) =>
        scenery.PlaceCratePinnedByPad("TooClose", FixtureLayout.Origin(FixtureSlot.TooClose));

    /// <summary>
    /// The pad stands in the gap between two pillars: inside the bounding box zone, but out of reach of
    /// the pillars' shape. Removed only with the BoundingBox zone.
    /// </summary>
    private static void AddTwinPillarsScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.TwinPillars);
        scenery.Mods.Target.Interior.Place("T_TwinPillars", scenery.Bases.TargetTwinPillars, origin);
        scenery.Mods.Rival.Interior.Place("R_TwinPillars_GapPad", scenery.Bases.RivalPad, origin);
    }

    /// <summary>A target without a model is judged by its Object Bounds as a box, whatever the zone shape.</summary>
    private static void AddBoundsOnlyScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.BoundsOnly);
        scenery.Mods.Target.Interior.Place("T_BoundsOnly", scenery.Bases.TargetBoundsOnly, origin);
        scenery.PlacePinningPad("BoundsOnly", origin, FixtureLayout.PinningPadOffset);
    }

    /// <summary>The rival crate is ignored as a replacement of the target crate, so the target stays.</summary>
    private static void AddReplacementScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.Replacement);
        scenery.PlaceTargetCrate("Replaced", origin);
        scenery.Mods.Rival.Interior.Place("R_Replacement_Crate", scenery.Bases.RivalCrate, origin + ReplacementShift);
    }
}
