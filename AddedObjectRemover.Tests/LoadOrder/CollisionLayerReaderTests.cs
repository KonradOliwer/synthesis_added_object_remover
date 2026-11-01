using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

public sealed class CollisionLayerReaderTests
{
    [Fact]
    public void EachLayerIsReadWithItsTriggerAndNavmeshObstacleFlagsAndTheLayerNumbersItCollidesWith()
    {
        var mod = TestCollisionLayers.AddTo(new SkyrimMod(ModKey.FromNameAndExtension("Layers.esp"), SkyrimRelease.SkyrimSE));

        var layers = CollisionLayerReader.Read(mod.ToImmutableLinkCache());

        var character = (uint)CollisionLayer.CharController;
        Assert.Equal(
            [
                $"{(uint)CollisionLayer.Static} False False {character}",
                $"{(uint)CollisionLayer.Trigger} True False {character}",
                $"{character} False False ",
                $"{(uint)CollisionLayer.CollisionBox} False False {character}",
                $"{(uint)CollisionLayer.NavCut} False True ",
            ],
            layers.Values.OrderBy(layer => layer.Index).Select(layer => $"{layer.Index} {layer.IsTrigger} {layer.IsNavmeshObstacle} {string.Join(",", layer.CollidesWith)}"));
    }
}
