using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The plugins of the fixture load order. The target masters only the base game: a master of the
/// target counts as ignored, so the other mod cannot be one. The target's override of the other
/// mod's record (see <see cref="FixtureStandingScene"/>) therefore lacks that master, which
/// in-memory plugins allow.
/// </summary>
internal sealed class FixtureMods
{
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string OtherModPlugin = "Rival.esp";
    private const string PatchedPlugin = "Patched.esp";
    private const string TargetPatchPlugin = "TargetPatch.esp";
    private const string KeepListEditorId = "TargetPatch_KeepList";

    private readonly FormList _keepList;

    private FixtureMods()
    {
        BaseGame = new FixtureMod(BaseGamePlugin);
        Target = new FixtureMod(FixtureWorld.TargetPlugin, BaseGame);
        OtherMod = new FixtureMod(OtherModPlugin, BaseGame, Target);
        Patched = new FixtureMod(PatchedPlugin, BaseGame);
        TargetPatch = new FixtureMod(TargetPatchPlugin, BaseGame, Target, Patched);
        Excluded = new FixtureMod(FixtureWorld.ExcludedPlugin, BaseGame);
        _keepList = TargetPatch.AddFormList(KeepListEditorId);
    }

    public FixtureMod BaseGame { get; }
    public FixtureMod Target { get; }
    public FixtureMod OtherMod { get; }

    /// <summary>Another mod that a compatibility patch links to the target, so it is ignored.</summary>
    public FixtureMod Patched { get; }

    /// <summary>The compatibility patch; also holds a non-placed record that references target objects.</summary>
    public FixtureMod TargetPatch { get; }

    public FixtureMod Excluded { get; }

    /// <summary>Lowest priority first.</summary>
    public IReadOnlyList<ISkyrimMod> LoadOrder => [BaseGame.Mod, Target.Mod, OtherMod.Mod, Patched.Mod, TargetPatch.Mod, Excluded.Mod];

    /// <summary>Every plugin that places records, with its copy of the shared spaces.</summary>
    public static FixtureMods Create()
    {
        var mods = new FixtureMods();
        var spaces = FixtureSpaces.CreateIn(mods.BaseGame);
        foreach (var mod in new[] { mods.Target, mods.OtherMod, mods.Patched, mods.Excluded }) spaces.AddOverridesTo(mod);
        return mods;
    }

    /// <summary>Makes a non-placed record of the compatibility patch reference the placed target object.</summary>
    public void ReferenceFromKeepList(IPlacedGetter targetObject) =>
        _keepList.Items.Add(new FormLink<ISkyrimMajorRecordGetter>(targetObject.FormKey));
}
