using System.Numerics;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Scenes with another mod's placed NPCs, which are sized by their Object Bounds.</summary>
internal static class FixtureNpcScenes
{
    /// <summary>Beside the crate's side with a gap, so the NPC's body does not reach into it.</summary>
    private static readonly Vector3 BesideCrate = new(70f, 0f, 0f);

    public static void AddAll(FixtureScenery scenery)
    {
        AddStuckScene(scenery);
        AddClearScene(scenery);
    }

    /// <summary>The NPC stands in the crate: too close unless NPCs are ignored.</summary>
    private static void AddStuckScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.NpcStuck);
        scenery.PlaceTargetCrate("NpcStuck", origin);
        scenery.Mods.Rival.Interior.PlaceNpc("R_NpcStuck", scenery.Bases.RivalNpc, origin);
    }

    /// <summary>The NPC stands beside the crate: only counts when NPCs count like objects.</summary>
    private static void AddClearScene(FixtureScenery scenery)
    {
        var origin = FixtureLayout.Origin(FixtureSlot.NpcClear);
        scenery.PlaceTargetCrate("NpcClear", origin);
        scenery.Mods.Rival.Interior.PlaceNpc("R_NpcClear", scenery.Bases.RivalNpc, origin + BesideCrate);
    }
}
