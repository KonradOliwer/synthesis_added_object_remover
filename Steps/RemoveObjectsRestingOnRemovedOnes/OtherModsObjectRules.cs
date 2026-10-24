using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Which other mods' objects the step judges, which of them the patch can change and which it may remove, and the meshes and
/// boxes of the objects it reads.
/// </summary>
internal sealed class OtherModsObjectRules(ModTrustTable trust, LoadOrderPlugins plugins, IBaseObjectShapes shapes)
{
    /// <summary>
    /// Judged: an object of a base game plugin, a master of the cleaned mod or a mod a trust rule names itself, with a solid mesh,
    /// not a placed NPC or creature, which stands on its own feet. An object belongs to such a plugin when it created the record or
    /// its winning version comes from it; objects of untrusted plugins, of plugins only masters of other trusted mods, and of
    /// plugins listed as never trusted are ignored. The cleaned mod's records that a later plugin overrides are left to that
    /// plugin, as everywhere else. Only visible objects reach the step: an invisible object or marker rests on nothing in game.
    /// </summary>
    public bool IsJudged(OtherObject other)
    {
        var shape = shapes.Of(other.Base);
        return other.Key.Plugin != trust.CleanedMod
            && !other.IsPlacedNpc
            && shape.MeshPath is not null
            && !shape.EffectOnlyMesh
            && (BelongsToJudgedPlugin(other.Key.Plugin) || BelongsToJudgedPlugin(other.WinningMod));
    }

    private bool BelongsToJudgedPlugin(PluginName plugin) =>
        !trust.IsNeverTrusted(plugin) && (trust.IsBaseGame(plugin) || trust.IsMasterOfCleanedMod(plugin) || trust.IsTrustedItself(plugin));

    /// <summary>The mesh of a judged object.</summary>
    public string MeshPathOf(OtherObject other) =>
        shapes.Of(other.Base).MeshPath ?? throw new InvalidOperationException($"The other mod's object {other.Key} has no mesh but was judged.");

    public OrientedBox BoxOf(OtherObject other, PlacedTransform transform) => OrientedBox.FromLocal(shapes.Of(other.Base).Box, transform);

    /// <summary>The mesh of a target object that can hold up others or rest on them.</summary>
    public string MeshPathOf(TargetObject target) =>
        shapes.Of(target.Base).MeshPath ?? throw new InvalidOperationException($"The target object {target.Key} has no mesh but was asked for its support.");

    public OrientedBox BoxOf(TargetObject target) => OrientedBox.FromLocal(shapes.Of(target.Base).Box, target.Transform);

    /// <summary>Where a target object that can hold up others stands: a visible one with a mesh; null for any other.</summary>
    public BoxInSpace? SupportingBoxOf(TargetObject target) =>
        shapes.VisibilityOf(target).IsVisible && shapes.Of(target.Base).MeshPath is not null ? new BoxInSpace(target.SpaceKey, BoxOf(target)) : null;

    /// <summary>A plugin listed after the patch beats what the patch writes, so the patch cannot change its records.</summary>
    public bool PatchCanChange(OtherObject other) => !plugins.LoadsAfterPatch(other.WinningMod);

    /// <summary>
    /// What keeps the object from being removed: every role it has. A persistent reference is one that quests, packages and
    /// scripts of other records can point to, which stands in for the links from non-placed records into other plugins' records,
    /// which are not read.
    /// </summary>
    public static ReferenceRoles RemovalBlockers(OtherObject other) => other.Roles;
}
