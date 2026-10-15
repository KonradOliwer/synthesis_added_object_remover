using System.Numerics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Run.RunAllSteps.RunCaches;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Determinism;

public class TwoCreationPointsOfCachesTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Creation.esp");

    [Fact]
    public void AddingTheObjectCachesCreatesANewObjectAndLeavesTheLoadOrderCachesAsTheyWere()
    {
        var dataFolder = Directory.CreateTempSubdirectory("aor-caches-");
        try
        {
            var loadOrder = CreateLoadOrderCaches(dataFolder.FullName);
            var shapes = loadOrder.Shapes;
            var meshFiles = loadOrder.MeshFiles;

            var first = AddObjectCaches(loadOrder);
            var second = AddObjectCaches(loadOrder);

            Assert.NotSame(first, second);
            Assert.NotSame(first.VisibleTargets, second.VisibleTargets);
            Assert.NotSame(first.OtherModObjects, second.OtherModObjects);
            Assert.Same(shapes, loadOrder.Shapes);
            Assert.Same(meshFiles, loadOrder.MeshFiles);
            Assert.Same(loadOrder.Shapes, first.Shapes);
            Assert.Same(loadOrder.Shapes, second.Shapes);
            Assert.Same(loadOrder.Triangles, first.Triangles);
        }
        finally
        {
            dataFolder.Delete(recursive: true);
        }
    }

    private static CachesOfTheRun AddObjectCaches(LoadOrderCaches loadOrder) =>
        loadOrder.AddObjectCaches(
            TestScenes.CreateWorld([], []),
            RemovalCauseSelector.MeasurementRules(new UnexpectedErrors()),
            new Execution(1));

    [Fact]
    public void TheCachesUseTheGivenBodyRules()
    {
        var dataFolder = Directory.CreateTempSubdirectory("aor-caches-");
        try
        {
            var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
            var npc = mod.Npcs.AddNew();
            var plantedBody = new NpcBody(NpcSizeSource.BodyMesh, new Box(Vector3.Zero, new Vector3(1, 2, 3)), null);
            var plantedRules = TooCloseObjects.NpcBodyRules with { CreateOwnBodyResolver = (_, _) => _ => plantedBody };

            var planted = CreateLoadOrderCaches(dataFolder.FullName, mod, plantedRules).Bodies.GetBodies(npc.FormKey.ToRecordKey());

            Assert.Equal([plantedBody], planted?.Bodies);
        }
        finally
        {
            dataFolder.Delete(recursive: true);
        }
    }

    private static LoadOrderCaches CreateLoadOrderCaches(string dataFolder) =>
        CreateLoadOrderCaches(dataFolder, new SkyrimMod(Mod, SkyrimRelease.SkyrimSE), TooCloseObjects.NpcBodyRules);

    private static LoadOrderCaches CreateLoadOrderCaches(string dataFolder, SkyrimMod mod, NpcBodyRules npcBodyRules)
    {
        var linkCache = mod.ToImmutableLinkCache();
        return LoadOrderCaches.Create(
            new MeshFilesFactory(dataFolder, GameRelease.SkyrimSE, [Mod]),
            new UnexpectedErrors(),
            new NpcRecordsPlugin(linkCache),
            new BaseFactsReader(linkCache),
            npcBodyRules,
            TooCloseObjects.PlacedNpcRules);
    }
}
