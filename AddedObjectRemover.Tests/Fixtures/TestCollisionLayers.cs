using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using MutagenLayer = Mutagen.Bethesda.Skyrim.CollisionLayer;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>
/// Collision layer records as the base game has them: the static and the collision box layers collide with the character controller's,
/// the trigger layer does too but is flagged as trigger volume and sensor, and the navmesh cut layer collides with nothing but is
/// flagged as navmesh obstacle.
/// </summary>
internal static class TestCollisionLayers
{
    private const uint FirstId = 0x9C0;

    /// <summary>Adds the layers to the mod, with ids from <see cref="FirstId"/> on.</summary>
    public static SkyrimMod AddTo(SkyrimMod mod)
    {
        var character = Add(mod, FirstId, CollisionLayer.CharController, flags: 0, collidesWith: []);
        Add(mod, FirstId + 1, CollisionLayer.Static, flags: 0, [character]);
        Add(mod, FirstId + 2, CollisionLayer.CollisionBox, flags: 0, [character]);
        Add(mod, FirstId + 3, CollisionLayer.Trigger, MutagenLayer.Flag.TriggerVolume | MutagenLayer.Flag.Sensor, [character]);
        Add(mod, FirstId + 4, CollisionLayer.NavCut, MutagenLayer.Flag.NavmeshObstacle, collidesWith: []);
        return mod;
    }

    private static FormKey Add(SkyrimMod mod, uint id, CollisionLayer index, MutagenLayer.Flag flags, IEnumerable<FormKey> collidesWith)
    {
        var layer = new MutagenLayer(new FormKey(mod.ModKey, id), SkyrimRelease.SkyrimSE) { Index = (uint)index, Flags = flags, CollidesWith = [] };
        layer.CollidesWith.AddRange(collidesWith.Select(key => new FormLink<ICollisionLayerGetter>(key)));
        mod.CollisionLayers.Add(layer);
        return layer.FormKey;
    }
}
