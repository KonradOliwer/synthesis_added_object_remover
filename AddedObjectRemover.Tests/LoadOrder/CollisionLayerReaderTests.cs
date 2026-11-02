using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

public sealed class CollisionLayerReaderTests
{
    /// <summary>A layer number no collision layer record of the load order has.</summary>
    private const uint UnknownLayer = 999;

    /// <summary>How many askers ask about a layer at once, each of the six layers asked about as often.</summary>
    private const int AskersAtOnce = 60;

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

    [Fact]
    public void ALayerRecordTheLibraryCannotParseFailsAsMalformedUserData()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var plugin = BrokenCollisionLayerData.WriteAndOpen(
                TestCollisionLayers.AddTo(new SkyrimMod(ModKey.FromNameAndExtension("Layers.esp"), SkyrimRelease.SkyrimSE)), folder);

            var failure = Assert.Throws<MalformedRecordException>(() => CollisionLayerReader.Read(plugin.ToImmutableLinkCache()));

            Assert.True(Failures.IsRecoverable(failure));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The layers are read when the first of many askers at once asks about one, and once. A layer blocks actors when it is a
    /// navmesh obstacle, or collides with characters and is no trigger; a number no record has blocks.
    /// </summary>
    [Fact]
    public void TheShapesReadTheLayersOnlyWhenALayerIsFirstAskedAboutAndThenOnce()
    {
        var mod = TestCollisionLayers.AddTo(new SkyrimMod(ModKey.FromNameAndExtension("Layers.esp"), SkyrimRelease.SkyrimSE));
        var linkCache = mod.ToImmutableLinkCache();
        var reads = 0;
        var meshFiles = new MeshFilesFactory(AppContext.BaseDirectory, GameRelease.SkyrimSE, [mod.ModKey.ToPluginName()]).Open(BaseObjectShapes.SolidShapes);
        var shapes = new BaseObjectShapes(TestBaseFacts.Reader(linkCache), meshFiles, () =>
        {
            Interlocked.Increment(ref reads);
            return CollisionLayerReader.Read(linkCache);
        });
        var readsBeforeAsking = reads;

        uint[] layers = [(uint)CollisionLayer.Static, (uint)CollisionLayer.Trigger, (uint)CollisionLayer.CharController, (uint)CollisionLayer.CollisionBox, (uint)CollisionLayer.NavCut, UnknownLayer];
        var blocks = new bool[AskersAtOnce];
        Parallel.For(0, AskersAtOnce, asker => blocks[asker] = shapes.LayerBlocksActors(layers[asker % layers.Length]));

        bool[] blocksByLayer = [true, false, false, true, true, true];
        Assert.Equal((0, 1), (readsBeforeAsking, reads));
        Assert.Equal(Enumerable.Range(0, AskersAtOnce).Select(asker => blocksByLayer[asker % layers.Length]), blocks);
    }
}
