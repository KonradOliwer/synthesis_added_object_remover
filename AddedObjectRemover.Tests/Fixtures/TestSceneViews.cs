namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The views the select and too-close steps build over a scene's objects.</summary>
internal static class TestSceneViews
{
    public static IOtherModObjectPositions OtherModObjectPositions(this IObjectCaches scene) => new OtherModObjectPositions(scene.OtherModObjects);

    public static INpcsThatCanSpawn NpcsThatCanSpawn(this IObjectCaches scene, Replacements replaced) => new NpcsThatCanSpawn(scene.PlacedNpcs, replaced);

    public static IObjectsThatCanCauseRemovals ObjectsThatCanCauseRemovals(this IObjectCaches scene, Replacements replaced, NpcHandling npcs) =>
        new ObjectsThatCanCauseRemovals(scene.OtherModObjects, replaced, npcs);
}
