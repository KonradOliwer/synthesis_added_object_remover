using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The base objects of the fixture, each in the plugin that places it.</summary>
internal sealed record FixtureBases(
    FormKey TargetCrate,
    FormKey TargetTwinPillars,
    FormKey TargetBoundsOnly,
    FormKey TargetDoor,
    FormKey TargetMarker,
    FormKey TargetLight,
    FormKey TargetSoundMarker,
    FormKey OtherModPad,
    FormKey OtherModCrate,
    FormKey OtherModBuilding,
    FormKey OtherModNpc,
    FormKey BaseGamePad,
    FormKey BaseGameCrate,
    FormKey PatchedPad,
    FormKey ExcludedPad)
{
    private const float NpcHalfWidth = 16f;
    private const float NpcHeight = 128f;

    public static FixtureBases Create(FixtureMods mods)
    {
        var target = mods.Target;
        var otherMod = mods.OtherMod;
        var otherModRace = otherMod.AddRace("Rival_Race");
        return new FixtureBases(
            TargetCrate: target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel),
            TargetTwinPillars: target.AddModeledStatic("T_TwinPillarsBase", FixtureMeshes.TwinPillarsModel),
            TargetBoundsOnly: target.AddBoundsOnlyStatic("T_BoundsOnlyBase", FixtureMeshes.CrateBox),
            TargetDoor: target.AddModeledDoor("T_DoorBase", FixtureMeshes.CrateModel),
            TargetMarker: target.AddMarkerStatic("T_XMarkerBase"),
            TargetLight: target.AddModelessLight("T_LightBase"),
            TargetSoundMarker: target.AddSoundMarker("T_SoundMarkerBase"),
            OtherModPad: otherMod.AddModeledStatic("R_PadBase", FixtureMeshes.PadModel),
            OtherModCrate: otherMod.AddModeledStatic("R_CrateBase", FixtureMeshes.CrateModel),
            OtherModBuilding: otherMod.AddModeledStatic("R_BuildingBase", FixtureMeshes.BuildingModel),
            OtherModNpc: otherMod.AddNpc("R_NpcBase", otherModRace, NpcBounds),
            BaseGamePad: mods.BaseGame.AddModeledStatic("Sky_PadBase", FixtureMeshes.PadModel),
            BaseGameCrate: mods.BaseGame.AddModeledStatic("Sky_CrateBase", FixtureMeshes.CrateModel),
            PatchedPad: mods.Patched.AddModeledStatic("Patched_PadBase", FixtureMeshes.PadModel),
            ExcludedPad: mods.Excluded.AddModeledStatic("Excluded_PadBase", FixtureMeshes.PadModel));
    }

    private static Box NpcBounds => new(new Vector3(-NpcHalfWidth, -NpcHalfWidth, 0f), new Vector3(NpcHalfWidth, NpcHalfWidth, NpcHeight));
}
