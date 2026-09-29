using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Records that must not cause a removal because of who made them or what state they are in.
/// Each crate sits in its own row, with a pad beside it that would make it too close if it counted.
/// </summary>
internal static class FixtureStandingScene
{
    private const float RowSpacing = 800f;

    public static void Add(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.Standing);
        var row = 0;
        Vector3 NextRow() => origin + new Vector3(0f, row++ * RowSpacing, 0f);

        AddOverriddenLater(scenery, NextRow());
        AddInitiallyDisabled(scenery, NextRow());
        AddOverriddenByTarget(scenery, NextRow());
        AddNearForeignPad(scenery, "BaseGame", scenery.Mods.BaseGame, scenery.Bases.BaseGamePad, NextRow());
        AddNearForeignPad(scenery, "Patched", scenery.Mods.Patched, scenery.Bases.PatchedPad, NextRow());
        AddNearForeignPad(scenery, "Excluded", scenery.Mods.Excluded, scenery.Bases.ExcludedPad, NextRow());
    }

    /// <summary>A later plugin overrides the target's record: the target's own version is not checked.</summary>
    private static void AddOverriddenLater(FixtureScenery scenery, Vector3 position)
    {
        var crate = scenery.PlaceCratePinnedByPad("OverriddenLater", position);
        scenery.Mods.Rival.Interior.PlaceCopyOf(crate);
    }

    /// <summary>A hidden target record is never checked.</summary>
    private static void AddInitiallyDisabled(FixtureScenery scenery, Vector3 position)
    {
        var crate = scenery.PlaceCratePinnedByPad("InitiallyDisabled", position);
        crate.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;
    }

    /// <summary>The target plugin overrides the rival's pad, which makes it a replacement, not a clash.</summary>
    private static void AddOverriddenByTarget(FixtureScenery scenery, Vector3 position)
    {
        const string name = "OverriddenByTarget";
        var pad = scenery.PlacePinningPad(name, position, FixtureLayout.PinningPadOffset);
        scenery.PlaceTargetCrate(name, position);
        scenery.Mods.Target.Interior.PlaceCopyOf(pad);
    }

    /// <summary>Pads of the base game, of a mod a compatibility patch links to the target, and of an excluded plugin never count.</summary>
    private static void AddNearForeignPad(FixtureScenery scenery, string name, FixtureMod padOwner, FormKey padBase, Vector3 position)
    {
        padOwner.Interior.Place($"{name}_Pad", padBase, position + FixtureLayout.PinningPadOffset);
        scenery.PlaceTargetCrate($"Near{name}Pad", position);
    }
}
