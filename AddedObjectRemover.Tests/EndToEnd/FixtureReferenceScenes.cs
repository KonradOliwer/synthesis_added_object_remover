using System.Numerics;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Scenes about target objects that stay or go together because other records link to them.</summary>
internal static class FixtureReferenceScenes
{
    public static void AddAll(FixtureScenery scenery)
    {
        AddLinkedGroupScene(scenery);
        AddReferencedByPlacedScene(scenery);
        AddReferencedByNonPlacedScene(scenery);
        AddTeleportDoorsScene(scenery);
    }

    /// <summary>The far crate has the too-close one as Enable Parent, so it goes with it.</summary>
    private static void AddLinkedGroupScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.LinkedGroup);
        var near = scenery.PlaceCratePinnedByPad("LinkedNear", origin);
        var far = scenery.PlaceTargetCrate("LinkedFar", origin + FixtureLayout.FarEast);
        far.EnableParent = FixtureScenery.EnableParentOf(near);
    }

    /// <summary>A rival record is enabled by the too-close crate, so the crate stays.</summary>
    private static void AddReferencedByPlacedScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.ReferencedByPlaced);
        var crate = scenery.PlaceCratePinnedByPad("ReferencedByPlaced", origin);
        var child = scenery.Mods.Rival.Interior.Place("R_EnableParentChild", scenery.Bases.RivalPad, origin + FixtureLayout.FarEast);
        child.EnableParent = FixtureScenery.EnableParentOf(crate);
    }

    /// <summary>A form list of the compatibility patch holds the too-close crate, so the crate stays.</summary>
    private static void AddReferencedByNonPlacedScene(FixtureScenery scenery)
    {
        var crate = scenery.PlaceCratePinnedByPad("ReferencedByFormList", FixtureLayout.Origin(FixtureSlot.ReferencedByNonPlaced));
        scenery.Mods.ReferenceFromKeepList(crate);
    }

    /// <summary>A pair of teleport doors, the first too close: both stay, in the cell's persistent list.</summary>
    private static void AddTeleportDoorsScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.TeleportDoors);
        var farPosition = origin + FixtureLayout.FarEast;
        scenery.PlacePinningPad("TeleportDoorA", origin, FixtureLayout.PinningPadOffset);
        var doorA = scenery.Mods.Target.Interior.Place("T_TeleportDoorA", scenery.Bases.TargetDoor, origin, persistent: true);
        var doorB = scenery.Mods.Target.Interior.Place("T_TeleportDoorB", scenery.Bases.TargetDoor, farPosition, persistent: true);
        doorA.TeleportDestination = CreateDestination(doorB, farPosition);
        doorB.TeleportDestination = CreateDestination(doorA, origin);
    }

    private static TeleportDestination CreateDestination(IPlacedObjectGetter door, Vector3 position)
    {
        var destination = new TeleportDestination { Position = new P3Float(position.X, position.Y, position.Z) };
        destination.Door.SetTo(door.FormKey);
        return destination;
    }
}
