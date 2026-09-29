using System.Numerics;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Scenes about what goes with a removed object: the objects touching it, and the objects resting on it.</summary>
internal static class FixtureFollowUpScenes
{
    private const float TouchGap = 4f;
    private const int ChainLength = 3;

    /// <summary>The seed lies to the left of the object resting on it, so it holds up only a fifth of that object's underside.</summary>
    private const float PartialSeedShift = -80f;

    /// <summary>The base game floor under the right half of the object resting partly on the seed.</summary>
    private const float FloorShift = 50f;

    private const float SupportRowSpacing = 1000f;

    private static readonly Vector3 OnTopOfCrate = new(0f, 0f, FixtureMeshes.CrateSize);

    public static void AddAll(FixtureScenery scenery)
    {
        AddTouchChainScene(scenery);
        AddSupportLossScene(scenery);
    }

    /// <summary>
    /// A seed too close to a pad, and a chain of crates each touching the one before. The last is
    /// referenced by a rival record, so it stays.
    /// </summary>
    private static void AddTouchChainScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.TouchChain);
        scenery.PlacePinningPad("TouchSeed", origin, -FixtureLayout.PinningPadOffset);
        scenery.PlaceTargetCrate("TouchSeed", origin);

        var step = new Vector3(FixtureMeshes.CrateSize + TouchGap, 0f, 0f);
        for (var link = 1; link < ChainLength; link++) scenery.PlaceTargetCrate($"TouchLink{link}", origin + link * step);
        var referenced = scenery.PlaceTargetCrate("TouchReferenced", origin + ChainLength * step);

        var child = scenery.Mods.Rival.Interior.Place("R_TouchEnableParentChild", scenery.Bases.RivalPad, origin + FixtureLayout.FarNorth);
        child.EnableParent = FixtureScenery.EnableParentOf(referenced);
    }

    /// <summary>
    /// A crate resting wholly on a removed seed, and another resting on a removed seed with a base
    /// game floor under most of its underside.
    /// </summary>
    private static void AddSupportLossScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.SupportLoss);
        AddResting(scenery, origin);
        AddRestingPartlyOnFloor(scenery, origin + new Vector3(0f, SupportRowSpacing, 0f));
    }

    private static void AddResting(FixtureScenery scenery, Vector3 origin)
    {
        scenery.PlaceCratePinnedByPad("SupportSeed", origin);
        scenery.PlaceTargetCrate("SupportedByOnlyTheSeed", origin + OnTopOfCrate);
    }

    private static void AddRestingPartlyOnFloor(FixtureScenery scenery, Vector3 origin)
    {
        var seedPosition = origin + new Vector3(PartialSeedShift, 0f, 0f);
        scenery.PlacePinningPad("SupportSeedPartial", seedPosition, -FixtureLayout.PinningPadOffset);
        scenery.PlaceTargetCrate("SupportSeedPartial", seedPosition);
        scenery.Mods.BaseGame.Interior.Place("Sky_SupportFloor", scenery.Bases.BaseGameCrate, origin + new Vector3(FloorShift, 0f, 0f));
        scenery.PlaceTargetCrate("SupportedPartlyBySeed", origin + OnTopOfCrate);
    }
}
