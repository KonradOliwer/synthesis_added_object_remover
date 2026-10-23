using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The load order of the end-to-end tests: generated in-memory mods whose scenes each make one rule
/// of the patcher act. The meshes they use are written as loose NIFs into a Data folder.
/// </summary>
internal sealed class FixtureWorld
{
    /// <summary>The mod being cleaned.</summary>
    public const string TargetName = "Target.esp";

    /// <summary>Listed in the settings' trusted mods and their masters.</summary>
    public const string TrustedPlugin = "Trusted.esp";

    /// <summary>The other mod; it requires the target and is listed in the settings' never trusted mods.</summary>
    public const string OtherModPlugin = "Rival.esp";

    private FixtureWorld(IReadOnlyList<ISkyrimMod> loadOrder)
    {
        LoadOrder = loadOrder;
    }

    /// <summary>The listed mods, lowest priority first, without the patch mod.</summary>
    public IReadOnlyList<ISkyrimMod> LoadOrder { get; }

    /// <summary>Builds all mods in memory and writes every NIF they use under <paramref name="dataFolder"/>.</summary>
    public static FixtureWorld Create(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var mods = FixtureMods.Create();
        var scenery = new FixtureScenery(mods, FixtureBases.Create(mods));

        FixtureShapeScenes.AddAll(scenery);
        FixtureNpcScenes.AddAll(scenery);
        FixtureReferenceScenes.AddAll(scenery);
        FixtureRestingObjectsScenes.AddAll(scenery);
        FixtureLeftBehindScenes.AddAll(scenery);
        FixtureStandingScene.Add(scenery);
        return new FixtureWorld(mods.LoadOrder);
    }
}
