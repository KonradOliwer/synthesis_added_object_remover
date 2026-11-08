using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// Which other mods' objects are judged, which of them the patch can change and which it may remove, and the meshes and
/// boxes of the objects it reads.
/// </summary>
internal sealed class OtherModsObjectRules(ModTrustTable trust, LoadOrderPlugins plugins, IBaseObjectShapes shapes)
{
    /// <summary>
    /// Judged: an object of a base game plugin, a master of the cleaned mod or a mod a trust rule names itself, with a solid mesh,
    /// not a placed NPC or creature, which stands on its own feet. An object belongs to such a plugin when it created the record or
    /// its winning version comes from it; objects of untrusted plugins, of plugins only masters of other trusted mods, and of
    /// plugins listed as never trusted are ignored. The cleaned mod's records that a later plugin overrides are left to that
    /// plugin, as everywhere else. Only visible objects are judged: an invisible object or marker rests on nothing in game.
    /// </summary>
    public bool IsJudged(OtherObject other) =>
        IsAnotherPluginsObjectThatCanRest(other) && (BelongsToJudgedPlugin(other.Key.Plugin) || BelongsToJudgedPlugin(other.WinningMod));

    /// <summary>
    /// Judged in the follow-up of objects leaving outside the removal rounds: an object that can rest on others, created by a plugin
    /// trusted in full other than the cleaned mod (a plugin trusted only as a master of another one too), whose winning version past
    /// the patches is of such a plugin or of the cleaned mod. A value a later plugin holds is that plugin's choice, so an object an
    /// untrusted plugin created or set where it stands is left as it is. The cleaned mod's own objects follow as target objects, except
    /// those a later plugin holds: they are left to that plugin, and the ground under them stays.
    /// </summary>
    public bool IsJudgedWhenObjectsLeave(OtherObject other) =>
        IsAnotherPluginsObjectThatCanRest(other) && trust.IsTrustedInFull(other.Key.Plugin) && trust.IsTrustedInFull(other.WinningModPastPatches);

    private bool IsAnotherPluginsObjectThatCanRest(OtherObject other) => other.Key.Plugin != trust.CleanedMod && CanRest(other);

    /// <summary>An object that can rest on another, whichever plugin it belongs to: one with a solid mesh, not a placed NPC or creature.</summary>
    public bool CanRest(OtherObject other)
    {
        var shape = shapes.Of(other.Base);
        return !other.IsPlacedNpc && shape.MeshPath is not null && !shape.EffectOnlyMesh;
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

    /// <param name="other">A judged object, where it stands.</param>
    public StandingObject StandingOf(OtherObject other) => StandingOf(other, other.Transform);

    /// <param name="other">An object that can rest, placed at <paramref name="at"/>.</param>
    public StandingObject StandingOf(OtherObject other, PlacedTransform at) =>
        new(other.SpaceKey, at, MeshPathOf(other), BoxOf(other, at), Supporter.Placed(other.Id));

    /// <param name="target">A visible target object with a mesh, where it stands.</param>
    public StandingObject StandingOf(TargetObject target) =>
        new(target.SpaceKey, target.Transform, MeshPathOf(target), BoxOf(target), Supporter.Target(target.Id.Index));

    /// <summary>Where a target object that can hold up others stands: a visible one with a mesh; null for any other.</summary>
    public BoxInSpace? SupportingBoxOf(TargetObject target) =>
        TargetVisibility.VisibilityOf(shapes, target).IsVisible && shapes.Of(target.Base).MeshPath is not null ? new BoxInSpace(target.SpaceKey, BoxOf(target)) : null;

    /// <summary>Whether an object of any plugin can hold up others: a visible one with a mesh.</summary>
    public bool CanHoldUp(OtherObject other) =>
        TargetVisibility.VisibilityOf(shapes, other).IsVisible && shapes.Of(other.Base).MeshPath is not null;

    /// <summary>A plugin listed after the patch beats what the patch writes, so the patch cannot change its records.</summary>
    public bool PatchCanChange(OtherObject other) => !plugins.LoadsAfterPatch(other.WinningMod);

    /// <summary>
    /// What keeps the object from being removed: every role it has. A persistent reference is one that quests, packages and
    /// scripts of other records can point to, which stands in for the links from non-placed records into other plugins' records,
    /// which are not read.
    /// </summary>
    public static ReferenceRoles RemovalBlockers(OtherObject other) => other.Roles;
}
