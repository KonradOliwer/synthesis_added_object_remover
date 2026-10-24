using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// Other mods' objects resting on the cleaned mod's objects, in an interior without terrain unless a scene says otherwise: a base
/// game floor, the cleaned mod's table standing on it, and other mods' objects on the table. The table is removed, or shrunk or
/// turned where it stands.
/// </summary>
public class OtherModsObjectsTests
{
    private const float TouchGap = 1f;
    private const float Threshold = 0.5f;
    private const float TableHeight = 40f;
    private const float BookHeight = 4f;
    private const float LanternHeight = 6f;
    private const float SlabHeight = 2f;
    private const float ShrunkScale = 0.8f;
    private const float DropTolerance = 0.01f;
    private const int Table = 0;
    private const int LanternOnTheBook = 1;

    private static readonly ModKey BaseGame = ModKey.FromNameAndExtension("Base.esm");
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly ModKey LateMod = ModKey.FromNameAndExtension("Late.esp");
    private static readonly ModKey CleanedModMaster = ModKey.FromNameAndExtension("CleanedModMaster.esm");
    private static readonly ModKey NeverTrustedMaster = ModKey.FromNameAndExtension("NeverTrustedMaster.esm");
    private static readonly ModKey UntrustedMod = ModKey.FromNameAndExtension("Untrusted.esp");
    private static readonly ModKey MasterOfOtherMod = ModKey.FromNameAndExtension("OtherMaster.esm");

    private static readonly TestStatic TableModel = new(
        new FormKey(TestTargets.TargetMod, 0x711), @"test\othermods\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, TableHeight))));

    private static readonly TestStatic BookModel = new(
        new FormKey(TestTargets.TargetMod, 0x712), @"test\othermods\book.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-8, -8, 0), new Vector3(8, 8, BookHeight))));

    private static readonly TestStatic LanternModel = new(
        new FormKey(TestTargets.TargetMod, 0x713), @"test\othermods\lantern.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, LanternHeight))));

    private static readonly TestStatic FloorModel = new(
        new FormKey(TestTargets.TargetMod, 0x714), @"test\othermods\floor.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-200, -200, -10), new Vector3(200, 200, 0))));

    /// <summary>As wide as the table, so the table covers its whole top.</summary>
    private static readonly TestStatic SlabModel = new(
        new FormKey(TestTargets.TargetMod, 0x715), @"test\othermods\slab.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, SlabHeight))));

    /// <summary>Taller than the table with a lantern on it.</summary>
    private static readonly TestStatic BlockModel = new(
        new FormKey(TestTargets.TargetMod, 0x716), @"test\othermods\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 50))));

    private static readonly IBaseObjectShapes Shapes =
        TestShapes.Create(TestTargets.TargetMod, "OtherModsObjectsData", TableModel, BookModel, LanternModel, FloorModel, SlabModel, BlockModel);

    private static readonly RemovalReason Clash = new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize());

    private static readonly LoadOrderPlugins Plugins = CreatePlugins();

    /// <summary>
    /// Base.esm is a base game plugin, CleanedModMaster.esm a master of the cleaned mod, NeverTrustedMaster.esm one listed as never
    /// trusted, Other.esp and Late.esp are trusted, OtherMaster.esm only as a master of Other.esp.
    /// </summary>
    private static readonly ModTrustTable Trust = TestModTrust.Of(
        TestTargets.TargetName,
        TestTargets.PatchName,
        trusted: [OtherMod.ToPluginName(), LateMod.ToPluginName()],
        untrusted: [UntrustedMod.ToPluginName()],
        baseGame: [BaseGame.ToPluginName()],
        cleanedModMasters: [CleanedModMaster.ToPluginName()],
        mastersOfTrusted: [MasterOfOtherMod.ToPluginName()],
        neverTrustedCleanedModMasters: [NeverTrustedMaster.ToPluginName()]);

    private static readonly Vector3 OnTheTable = new(5, 5, TableHeight);

    [Fact]
    public void AnObjectOnARemovedTableIsPulledDownOntoTheFloor()
    {
        var outcome = Assert.Single(Settle(WithFloor(Lantern(OtherMod, 1, OnTheTable)), RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround).Objects);

        Assert.Equal(OtherModsObjectFate.PulledDown, outcome.Fate);
        Assert.Equal(TableHeight, outcome.Drop, DropTolerance);
        Assert.Equal(0f, outcome.NewPosition.Z, DropTolerance);
        Assert.Equal(1f, outcome.LostShare, DropTolerance);
    }

    [Fact]
    public void AnObjectOnARemovedTableInAWorldspaceIsPulledDownOntoTheTerrain()
    {
        var corner = new Vector3(100, 100, 0);
        var scene = new Scene([CreateTable(corner)], [Lantern(OtherMod, 0, corner + OnTheTable)], OtherModsObjectsHandling.PullObjectToTheGround)
        {
            Terrain = FlatTerrainAtZero(),
        };

        var outcome = Assert.Single(Settle(scene, RemoveTable).Result.Objects);

        Assert.Equal(OtherModsObjectFate.PulledDown, outcome.Fate);
        Assert.Equal(0f, outcome.NewPosition.Z, DropTolerance);
    }

    [Fact]
    public void AnObjectOnAShrunkTableIsPulledDownOntoItsNewTop()
    {
        var outcome = Assert.Single(Settle(WithFloor(Lantern(OtherMod, 1, OnTheTable)), ShrinkTable, OtherModsObjectsHandling.PullObjectToTheGround).Objects);

        Assert.Equal(OtherModsObjectFate.PulledDown, outcome.Fate);
        Assert.Equal(TableHeight * (1f - ShrunkScale), outcome.Drop, DropTolerance);
    }

    /// <summary>Turned by 45 degrees, the table's corner no longer lies under the lantern, which falls to the floor.</summary>
    [Fact]
    public void AnObjectOffTheCornerOfATurnedTableIsPulledDownOntoTheFloorKeepingItsOwnTurn()
    {
        var lantern = TestShapes.Placed(OtherMod, 1, LanternModel.Base, new Vector3(19, 19, TableHeight), zRadians: 0.5f);

        var outcome = Assert.Single(Settle(WithFloor(lantern), TurnTable, OtherModsObjectsHandling.PullObjectToTheGround).Objects);

        Assert.Equal(OtherModsObjectFate.PulledDown, outcome.Fate);
        Assert.Equal((19f, 19f), (outcome.NewPosition.X, outcome.NewPosition.Y));
        Assert.Equal(0f, outcome.NewPosition.Z, DropTolerance);
    }

    [Fact]
    public void AStackOnARemovedTableLandsFromTheBottomUp()
    {
        List<OtherObject> others = [Floor(), Book(OtherMod, 1, OnTheTable), Lantern(OtherMod, 2, OnTheTable + new Vector3(0, 0, BookHeight))];

        var outcomes = Settle(others, RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround).Objects;

        Assert.Equal([1, 2], outcomes.Select(outcome => outcome.Object.Id.Index));
        Assert.All(outcomes, outcome => Assert.Equal(OtherModsObjectFate.PulledDown, outcome.Fate));
        Assert.Equal(0f, outcomes[0].NewPosition.Z, DropTolerance);
        Assert.Equal(BookHeight, outcomes[1].NewPosition.Z, DropTolerance);
    }

    [Fact]
    public void AnObjectWithNothingBelowItIsRemovedInstead()
    {
        var outcome = Assert.Single(Settle([Lantern(OtherMod, 0, OnTheTable)], RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround).Objects);

        Assert.Equal(OtherModsObjectFate.RemovedNothingBelow, outcome.Fate);
    }

    [Fact]
    public void APersistentObjectWithNothingBelowItIsLeftWhereItStands()
    {
        var lantern = Lantern(OtherMod, 0, OnTheTable) with { Roles = ReferenceRoles.Persistent };

        var outcome = Assert.Single(Settle([lantern], RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround).Objects);

        Assert.Equal(OtherModsObjectFate.LeftNothingBelowNotRemovable, outcome.Fate);
        Assert.Equal(ReferenceRoles.Persistent, outcome.Blocking);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithRemoveFloatingObjectAnObjectOnARemovedOrShrunkTableIsRemoved(bool removed)
    {
        var outcome = Assert.Single(
            Settle(WithFloor(Lantern(OtherMod, 1, OnTheTable)), removed ? RemoveTable : ShrinkTable, OtherModsObjectsHandling.RemoveFloatingObject).Objects);

        Assert.Equal(OtherModsObjectFate.Removed, outcome.Fate);
    }

    [Fact]
    public void WithRemoveFloatingObjectAnObjectAnotherObjectLinksToIsLeftAndStillHoldsUpWhatRestsOnIt()
    {
        var book = Book(OtherMod, 1, OnTheTable) with { Roles = ReferenceRoles.LinkedFromPlacedRecord };

        var outcomes = Settle(
            [Floor(), book, Lantern(OtherMod, 2, OnTheTable + new Vector3(0, 0, BookHeight))], RemoveTable, OtherModsObjectsHandling.RemoveFloatingObject).Objects;

        var outcome = Assert.Single(outcomes);
        Assert.Equal((1, OtherModsObjectFate.LeftNotRemovable, ReferenceRoles.LinkedFromPlacedRecord), (outcome.Object.Id.Index, outcome.Fate, outcome.Blocking));
    }

    [Fact]
    public void AnObjectHalfOnAnotherObjectKeepsBelowTheThresholdAndIsLeftAlone()
    {
        List<OtherObject> others = [Floor(), BaseGameTable(1, new Vector3(40, 0, 0)), Lantern(OtherMod, 2, new Vector3(20, 0, TableHeight))];

        Assert.Empty(Settle(others, RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround, threshold: 0.6f).Objects);
    }

    /// <summary>
    /// A base game slab under the table, as wide as it, loses the top half of its contacts but still rests on the floor; a lantern
    /// on the floor touching the table's side loses only what touched it from the side. Neither is left without support, and
    /// neither holds the table.
    /// </summary>
    [Theory]
    [InlineData(OtherModsObjectsHandling.KeepSupportingObject)]
    [InlineData(OtherModsObjectsHandling.RemoveFloatingObject)]
    [InlineData(OtherModsObjectsHandling.PullObjectToTheGround)]
    public void AnObjectUnderOrBesideARemovedTableIsUntouchedWithEveryChoice(OtherModsObjectsHandling handling)
    {
        const float lowThreshold = 0.1f;
        List<TargetObject> targets = [CreateTable(new Vector3(0, 0, SlabHeight))];
        List<OtherObject> others =
        [
            Floor(),
            TestShapes.Placed(BaseGame, 1, SlabModel.Base, Vector3.Zero),
            Lantern(BaseGame, 2, new Vector3(23.5f, 0, 0)),
        ];
        var scene = new Scene(targets, others, handling) { Threshold = lowThreshold };

        Assert.Empty(FindHeldUp(scene).Targets);
        Assert.Empty(Settle(scene, RemoveTable).Result.Objects);
    }

    /// <summary>
    /// The lantern stands half on the table and half sunk into a base game block beside it. The samples inside the block hold it
    /// there: from inside, the ray down would meet the block's underside, far below.
    /// </summary>
    [Fact]
    public void AnObjectPartlySunkIntoAnObjectThatStaysStillRestsOnIt()
    {
        List<OtherObject> others = [Floor(), TestShapes.Placed(BaseGame, 1, BlockModel.Base, new Vector3(40, 0, 0)), Lantern(OtherMod, 2, new Vector3(20, 0, TableHeight))];

        Assert.Empty(Settle(others, RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround, threshold: 0.1f).Objects);
    }

    [Fact]
    public void AnObjectOfABaseGamePluginOrOfAMasterOfTheCleanedModOnARemovedTableIsPulledDown()
    {
        var fromBaseGame = Assert.Single(Settle(WithFloor(Lantern(BaseGame, 1, OnTheTable)), RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround).Objects);
        var fromMaster = Assert.Single(Settle(WithFloor(Lantern(CleanedModMaster, 1, OnTheTable)), RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround).Objects);

        Assert.Equal([OtherModsObjectFate.PulledDown, OtherModsObjectFate.PulledDown], [fromBaseGame.Fate, fromMaster.Fate]);
    }

    [Theory]
    [InlineData(OtherModsObjectsHandling.RemoveFloatingObject)]
    [InlineData(OtherModsObjectsHandling.PullObjectToTheGround)]
    public void AnObjectOfAnUntrustedModOfAPluginTrustedOnlyAsAMasterOfAnotherModOrOfANeverTrustedMasterIsIgnored(OtherModsObjectsHandling handling)
    {
        Assert.Empty(Settle(WithFloor(Lantern(UntrustedMod, 1, OnTheTable)), RemoveTable, handling).Objects);
        Assert.Empty(Settle(WithFloor(Lantern(MasterOfOtherMod, 1, OnTheTable)), RemoveTable, handling).Objects);
        Assert.Empty(Settle(WithFloor(Lantern(NeverTrustedMaster, 1, OnTheTable)), RemoveTable, handling).Objects);
    }

    [Fact]
    public void WithKeepSupportingObjectATableUnderAnIgnoredObjectIsNotHeldUp()
    {
        Assert.Empty(FindHeldUp([CreateTable()], WithFloor(Lantern(UntrustedMod, 1, OnTheTable)), OtherModsObjectsHandling.KeepSupportingObject).Targets);
        Assert.Empty(FindHeldUp([CreateTable()], WithFloor(Lantern(MasterOfOtherMod, 1, OnTheTable)), OtherModsObjectsHandling.KeepSupportingObject).Targets);
        Assert.Empty(FindHeldUp([CreateTable()], WithFloor(Lantern(NeverTrustedMaster, 1, OnTheTable)), OtherModsObjectsHandling.KeepSupportingObject).Targets);
    }

    [Fact]
    public void AnObjectAPluginAfterThePatchWinsIsLeftAsItIs()
    {
        var outcome = Assert.Single(Settle(WithFloor(Lantern(LateMod, 1, OnTheTable)), RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround).Objects);

        Assert.Equal(OtherModsObjectFate.LeftWonAfterPatch, outcome.Fate);
    }

    [Fact]
    public void AnObjectOnlyBesideTheTableWithNothingBelowItIsNotRestingAndIsLeftAlone()
    {
        Assert.Empty(Settle([Lantern(OtherMod, 0, new Vector3(23.5f, 0, 10))], RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround).Objects);
    }

    [Fact]
    public void TheOutcomesDoNotDependOnTheThreadCount()
    {
        List<OtherObject> others = [Floor(), Book(OtherMod, 1, OnTheTable), Lantern(OtherMod, 2, OnTheTable + new Vector3(0, 0, BookHeight)), Lantern(OtherMod, 3, new Vector3(-10, -10, TableHeight))];

        var single = Settle(others, RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround, threads: 1).Objects;
        var parallel = Settle(others, RemoveTable, OtherModsObjectsHandling.PullObjectToTheGround, threads: 4).Objects;

        Assert.Equal(3, single.Length);
        Assert.Equal(single.ToArray(), parallel.ToArray());
    }

    /// <summary>
    /// A book of another mod on the removed table, a lantern of the cleaned mod on the book, and another mod's lantern on that
    /// lantern. The book is pulled down; the also-remove mode decides about the cleaned mod's lantern as about any object whose
    /// support is removed; once that lantern is removed, the other mod's lantern on it is judged again and pulled down onto the book.
    /// </summary>
    [Theory]
    [InlineData(FollowUpRemovalMode.Nothing)]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport)]
    public void ACleanedModsObjectOnAPulledObjectGoesAsTheAlsoRemoveModeSaysAndWhatRestsOnItIsJudgedAgain(FollowUpRemovalMode mode)
    {
        var (result, decisions) = Settle(LanternOnABookScene(mode, threads: 1), RemoveTable);

        var lantern = decisions.Of(new TargetId(LanternOnTheBook));
        if (mode == FollowUpRemovalMode.Nothing)
        {
            Assert.Null(lantern);
            Assert.Equal([1], result.Objects.Select(outcome => outcome.Object.Id.Index));
            return;
        }

        RemovalReason expected = mode == FollowUpRemovalMode.EverythingTouchingIt
            ? new RemovalReason.TouchingAnotherModsObject(new OtherId(1))
            : new RemovalReason.LostSupportWithAnotherModsObject(1f, new OtherId(1));
        var removed = Assert.IsType<Decision.Removed>(lantern);
        Assert.Equal(expected.GetType(), removed.Reason.GetType());
        Assert.Equal(new OtherId(1), removed.Reason switch
        {
            RemovalReason.TouchingAnotherModsObject touching => touching.Touched,
            RemovalReason.LostSupportWithAnotherModsObject lost => lost.MainSupporter,
            _ => default,
        });
        Assert.Equal([1, 2], result.Objects.Select(outcome => outcome.Object.Id.Index));
        Assert.All(result.Objects, outcome => Assert.Equal(OtherModsObjectFate.PulledDown, outcome.Fate));
        Assert.Equal(BookHeight, result.Objects[1].NewPosition.Z, DropTolerance);
    }

    [Fact]
    public void TheRoundsForTheCleanedModsObjectsDoNotDependOnTheThreadCount()
    {
        var single = Settle(LanternOnABookScene(FollowUpRemovalMode.OnlyWhatLosesItsSupport, threads: 1), RemoveTable).Result;
        var parallel = Settle(LanternOnABookScene(FollowUpRemovalMode.OnlyWhatLosesItsSupport, threads: 4), RemoveTable).Result;

        Assert.Equal(single.Objects.ToArray(), parallel.Objects.ToArray());
        Assert.Equal(single.ProposedRounds.SelectMany(round => round), parallel.ProposedRounds.SelectMany(round => round));
    }

    [Fact]
    public void WithKeepSupportingObjectNothingIsSettledAndOtherwiseNothingIsKept()
    {
        var others = WithFloor(Lantern(OtherMod, 1, OnTheTable));

        Assert.Same(OtherModsObjectsResult.SwitchedOff, Settle(others, RemoveTable, OtherModsObjectsHandling.KeepSupportingObject));
        Assert.Same(HeldUpTargets.SwitchedOff, FindHeldUp([CreateTable()], others, OtherModsObjectsHandling.PullObjectToTheGround));
    }

    [Fact]
    public void WithKeepSupportingObjectTheTableUnderAnotherModsObjectIsHeldUp()
    {
        var lantern = Lantern(OtherMod, 1, OnTheTable);

        var heldUp = FindHeldUp([CreateTable()], WithFloor(lantern), OtherModsObjectsHandling.KeepSupportingObject);

        Assert.Equal(StepOutcome.Performed, heldUp.Outcome);
        Assert.Equal(new RestingObject(lantern.Key, lantern.EditorId, KeptFor: null), Assert.Single(heldUp.Targets).Value);
    }

    [Fact]
    public void WithKeepSupportingObjectTheTableUnderTheCleanedModsBookUnderAnotherModsObjectIsHeldUpToo()
    {
        var lantern = Lantern(OtherMod, 1, OnTheTable + new Vector3(0, 0, BookHeight));
        List<TargetObject> targets = [CreateTable(), TestTargets.Create(1, TestTargets.At(OnTheTable), BookModel.Base, TestTargets.Space)];

        var heldUp = FindHeldUp(targets, WithFloor(lantern), OtherModsObjectsHandling.KeepSupportingObject);

        var onTheBook = new RestingObject(lantern.Key, lantern.EditorId, KeptFor: null);
        Assert.Equal(onTheBook, heldUp.Targets[new TargetId(1)]);
        Assert.Equal(new RestingObject(targets[1].Key, targets[1].EditorId, onTheBook), heldUp.Targets[new TargetId(Table)]);
    }

    [Fact]
    public void WithKeepSupportingObjectATableHoldingLessThanTheThresholdOrOnlyTouchingFromTheSideIsNotHeldUp()
    {
        List<OtherObject> halfOnIt = [Floor(), BaseGameTable(1, new Vector3(40, 0, 0)), Lantern(OtherMod, 2, new Vector3(20, 0, TableHeight))];
        List<OtherObject> beside = [Lantern(OtherMod, 0, new Vector3(23.5f, 0, 10))];

        Assert.Empty(FindHeldUp([CreateTable()], halfOnIt, OtherModsObjectsHandling.KeepSupportingObject, threshold: 0.6f).Targets);
        Assert.Empty(FindHeldUp([CreateTable()], beside, OtherModsObjectsHandling.KeepSupportingObject).Targets);
    }

    /// <summary>The objects of one scene and how the step is set.</summary>
    private sealed record Scene(List<TargetObject> Targets, IReadOnlyList<OtherObject> Others, OtherModsObjectsHandling Handling)
    {
        public FollowUpRemovalMode AlsoRemove { get; init; } = FollowUpRemovalMode.Nothing;
        public ITerrainHeights Terrain { get; init; } = TestGround.NoTerrain();
        public float Threshold { get; init; } = OtherModsObjectsTests.Threshold;
        public int Threads { get; init; } = 1;
    }

    /// <summary>The floor (id 0), the table (target 0) with the other mod's book (id 1) on it, the cleaned mod's lantern (target 1) on the book, and the other mod's lantern (id 2) on that lantern.</summary>
    private static Scene LanternOnABookScene(FollowUpRemovalMode mode, int threads)
    {
        var onTheBook = OnTheTable + new Vector3(0, 0, BookHeight);
        List<TargetObject> targets = [CreateTable(), TestTargets.Create(LanternOnTheBook, TestTargets.At(onTheBook), LanternModel.Base, TestTargets.Space)];
        List<OtherObject> others = [Floor(), Book(OtherMod, 1, OnTheTable), Lantern(OtherMod, 2, onTheBook + new Vector3(0, 0, LanternHeight))];
        return new Scene(targets, others, OtherModsObjectsHandling.PullObjectToTheGround) { AlsoRemove = mode, Threads = threads };
    }

    private static OtherModsObjectsResult Settle(
        IReadOnlyList<OtherObject> others,
        Func<RemovalDecisions, RemovalDecisions> decide,
        OtherModsObjectsHandling handling,
        float threshold = Threshold,
        int threads = 1) =>
        Settle(new Scene([CreateTable()], others, handling) { Threshold = threshold, Threads = threads }, decide).Result;

    /// <returns>The step's result, and the decisions with its rounds applied.</returns>
    private static (OtherModsObjectsResult Result, RemovalDecisions Decisions) Settle(Scene scene, Func<RemovalDecisions, RemovalDecisions> decide)
    {
        var caches = TestScenes.CreateWithSupportOnlyObjects(scene.Targets, scene.Others, Shapes, scene.Threads);
        var protection = ObjectsToKeep.Build(scene.Targets, [], TestTargets.References(scene.Targets.Count), TestBaseFacts.Unresolved);
        var decided = decide(RemovalDecisions.Start(protection, scene.Targets.Count));
        var asTheyStand = ClashingObjectFitter.PlaceAsTheyStand([.. scene.Targets], decided);
        var result = RestingObjects.SettleOtherModsObjects(
            [.. scene.Targets],
            asTheyStand,
            decided,
            protection,
            caches.VisibleTargets,
            caches.IndexVisibleTargets(asTheyStand),
            Trust,
            Plugins,
            new OtherModsObjectsSettings(scene.Handling, TouchGap, scene.Threshold),
            new AlsoRemoveSettings(scene.AlsoRemove, TouchGap, scene.Threshold, TouchChainsShown: false),
            Shapes,
            new TriangleStore(Shapes.ReadTriangles),
            scene.Terrain,
            caches.VisibleObjectsOfAnyPlugin,
            new Execution(scene.Threads));
        return (result, decided.ApplyRounds(RoundKind.AlsoRemove, result.ProposedRounds));
    }

    private static HeldUpTargets FindHeldUp(
        List<TargetObject> targets, IReadOnlyList<OtherObject> others, OtherModsObjectsHandling handling, float threshold = Threshold) =>
        FindHeldUp(new Scene(targets, others, handling) { Threshold = threshold });

    private static HeldUpTargets FindHeldUp(Scene scene)
    {
        var caches = TestScenes.CreateWithSupportOnlyObjects(scene.Targets, scene.Others, Shapes);
        return RestingObjects.FindHeldUpTargets(
            [.. scene.Targets],
            caches.VisibleTargets,
            Trust,
            Plugins,
            new OtherModsObjectsSettings(scene.Handling, TouchGap, scene.Threshold),
            Shapes,
            new TriangleStore(Shapes.ReadTriangles),
            scene.Terrain,
            caches.VisibleObjectsOfAnyPlugin,
            new Execution(scene.Threads));
    }

    private static RemovalDecisions RemoveTable(RemovalDecisions decisions) =>
        decisions.Apply(RoundKind.TooClose, [new ProposedRemoval(new TargetId(Table), Clash)]);

    private static RemovalDecisions ShrinkTable(RemovalDecisions decisions) =>
        decisions.ApplyFits([new ProposedFit(new TargetId(Table), Clash, new EulerPlacement(Vector3.Zero, Vector3.Zero, ShrunkScale))]);

    private static RemovalDecisions TurnTable(RemovalDecisions decisions) =>
        decisions.ApplyFits([new ProposedFit(new TargetId(Table), Clash, new EulerPlacement(Vector3.Zero, new Vector3(0, 0, MathF.PI / 4), 1f))]);

    private static TargetObject CreateTable() => CreateTable(Vector3.Zero);

    private static TargetObject CreateTable(Vector3 position) => TestTargets.Create(Table, TestTargets.At(position), TableModel.Base, TestTargets.Space);

    /// <summary>The floor first, with id 0, then the given object with id 1.</summary>
    private static List<OtherObject> WithFloor(OtherObject resting) => [Floor(), resting];

    private static OtherObject Floor() => TestShapes.Placed(BaseGame, 0, FloorModel.Base, Vector3.Zero);

    private static OtherObject BaseGameTable(int id, Vector3 position) => TestShapes.Placed(BaseGame, id, TableModel.Base, position);

    private static OtherObject Book(ModKey mod, int id, Vector3 position) => TestShapes.Placed(mod, id, BookModel.Base, position);

    private static OtherObject Lantern(ModKey mod, int id, Vector3 position) => TestShapes.Placed(mod, id, LanternModel.Base, position);

    /// <summary>Flat land at height 0 in the space's cell (0, 0), which reaches from 0 to 4096 on both axes.</summary>
    private static TerrainHeights FlatTerrainAtZero() =>
        TestGround.Terrain(
            new Dictionary<ExteriorCell, ILandscapeGetter>
            {
                [new ExteriorCell(TestTargets.Space, 0, 0)] = new Landscape(new FormKey(TestTargets.TargetMod, 0x300), SkyrimRelease.SkyrimSE) { VertexHeightMap = new LandscapeVertexHeightMap() },
            },
            new Dictionary<RecordKey, RecordKey> { [TestTargets.Space] = TestTargets.Space });

    private static LoadOrderPlugins CreatePlugins()
    {
        PluginName[] names =
        [
            BaseGame.ToPluginName(),
            CleanedModMaster.ToPluginName(),
            NeverTrustedMaster.ToPluginName(),
            TestTargets.TargetName,
            OtherMod.ToPluginName(),
            UntrustedMod.ToPluginName(),
            MasterOfOtherMod.ToPluginName(),
            TestTargets.PatchName,
            LateMod.ToPluginName(),
        ];
        return TestLoadOrderPlugins.Of(new KnownPluginNames(names), TestTargets.PatchName, [.. names.Select(name => new PluginListing(name, Loaded: true, Masters: []))]);
    }
}
