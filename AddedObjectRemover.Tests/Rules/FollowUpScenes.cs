using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The scenes of the removal steps' follow-up asked about the candidates of the restore leaving where they stand now, over in-memory
/// scenes on flat ground: a base game rock the cleaned mod moved, which leaves as an object of any plugin moved back to its earlier
/// place, and a crate of another plugin the cleaned mod moved far enough to count among its targets, which leaves as a target object
/// restored to its earlier version. Pebbles of the cleaned mod and other plugins' lanterns rest on them.
/// </summary>
internal static class FollowUpScenes
{
    public const float HalfSize = 20f;
    public const float Top = 2 * HalfSize;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("FollowUpLeavingShapes.esp");
    public static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\followupleaving\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-HalfSize), new Vector3(HalfSize))));
    public static readonly TestStatic Lantern = new(new FormKey(ShapeMod, 0x802), @"test\followupleaving\lantern.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));
    private static readonly TestStatic Pebble = new(new FormKey(ShapeMod, 0x803), @"test\followupleaving\pebble.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-1.5f, -1.5f, 0), new Vector3(1.5f, 1.5f, 3))));
    /// <summary>Another model of a block's shape and size.</summary>
    public static readonly TestStatic Boulder = new(new FormKey(ShapeMod, 0x804), @"test\followupleaving\boulder.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-HalfSize), new Vector3(HalfSize))));
    /// <summary>How far a plate's top lies above its bottom: twice the touch gap, so what rests on a plate floats once it is gone.</summary>
    public const float PlateThickness = 2f;

    private const float BowlHalfWidth = 25f;
    public const float BowlFloor = 4f;

    /// <summary>
    /// A bowl's width, as thin as <see cref="PlateThickness"/>, its bottom where a block's is: a bowl on it touches it with more than half
    /// of what it touches when a block rests on its floor.
    /// </summary>
    public static readonly TestStatic Plate = new(
        new FormKey(ShapeMod, 0x805),
        @"test\followupleaving\plate.nif",
        TestMeshes.BoxTriangles(new Box(new Vector3(-BowlHalfWidth, -BowlHalfWidth, -HalfSize), new Vector3(BowlHalfWidth, BowlHalfWidth, -HalfSize + PlateThickness))));

    /// <summary>
    /// Where the post of a bowl starts along x and y: a block in the middle of the bowl is this far beyond its own corner, within the
    /// touch gap along each axis, so the post lies in its contact area, and farther than the gap diagonally, so the post never touches it.
    /// </summary>
    private const float PostFrom = HalfSize + 0.8f;

    private const float PostHeight = 10f;

    /// <summary>A floor with its bottom where a block's is and a post on one corner, which rises beside a block resting on the floor.</summary>
    public static readonly TestStatic Bowl = new(
        new FormKey(ShapeMod, 0x806),
        @"test\followupleaving\bowl.nif",
        [
            .. TestMeshes.BoxTriangles(new Box(new Vector3(-BowlHalfWidth, -BowlHalfWidth, -HalfSize), new Vector3(BowlHalfWidth, BowlHalfWidth, -HalfSize + BowlFloor))),
            .. TestMeshes.BoxTriangles(new Box(new Vector3(PostFrom, PostFrom, -HalfSize + BowlFloor), new Vector3(BowlHalfWidth, BowlHalfWidth, -HalfSize + BowlFloor + PostHeight))),
        ]);

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(ShapeMod, "FollowUpLeavingData", Block, Lantern, Pebble, Boulder, Plate, Bowl));

    /// <summary>A base the game shows, whose record and mesh the shapes do not know, like a marker without a model.</summary>
    public static readonly BaseKey WithoutMesh = TestVisibility.TaggedBase(ObjectVisibility.Visible);

    public static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    public static readonly ModKey MasterMod = ModKey.FromNameAndExtension("Master.esm");
    private static readonly ModKey ListedMod = ModKey.FromNameAndExtension("Listed.esp");
    public static readonly ModKey LaterMod = ModKey.FromNameAndExtension("Later.esp");
    private static readonly ModKey OwnChangesMod = ModKey.FromNameAndExtension("OwnChanges.esp");
    private static readonly ModKey NeverMod = ModKey.FromNameAndExtension("Never.esp");
    private static readonly PluginName BaseGame = BaseGameMod.ToPluginName();

    private static readonly PluginName[] PluginList =
    [
        BaseGame, MasterMod.ToPluginName(), ListedMod.ToPluginName(), TestTargets.TargetName, LaterMod.ToPluginName(), OwnChangesMod.ToPluginName(),
        NeverMod.ToPluginName(), TestTargets.PatchName,
    ];

    private static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);

    /// <summary>Master.esm is trusted only as a master of the listed trusted mod.</summary>
    private static readonly ModTrustTable Trust = TestModTrust.Of(
        TestTargets.TargetName,
        TestTargets.PatchName,
        trusted: [ListedMod.ToPluginName()],
        untrusted: [LaterMod.ToPluginName()],
        trustedForOwnChanges: [OwnChangesMod.ToPluginName()],
        mastersOfTrusted: [MasterMod.ToPluginName()],
        baseGame: [BaseGame],
        neverTrusted: [NeverMod.ToPluginName()]);

    public static readonly Vector3 RockNow = new(500, 500, HalfSize);
    public static readonly Vector3 CrateNow = new(1000, 500, HalfSize);
    public static readonly Vector3 SecondRockNow = new(1500, 500, HalfSize);

    /// <summary>The rock's candidate index: the candidates are listed rock, crate, second rock.</summary>
    public const int RockCandidate = 0;
    public const int CrateCandidate = 1;
    public const int SecondRockCandidate = 2;
    public const int ThirdRockCandidate = 3;

    /// <summary>The cleaned mod's crate, the first target object.</summary>
    public const int CrateTarget = 0;

    /// <summary>The record number of a lantern, which no rock's record shares.</summary>
    public const int LanternIndex = 5;

    /// <summary>The record number of a block beside the rock.</summary>
    public const int BesideIndex = 6;

    /// <summary>The lantern's id: the rock is object 0, the others follow from 1.</summary>
    public static readonly OtherId FirstOther = new(1);

    /// <summary>The rock's key, as a list of the placed records that started a follow-up.</summary>
    public static readonly string RockStarter = RockKey(0, BaseGameMod).ToString();

    /// <summary>Each target object the follow-up writes, with the placed records that started it.</summary>
    public static List<(TargetId Target, string StartedBy)> WrittenTargets(FollowUpChecks checks) =>
        [.. checks.Written.Targets.Select(followed => (followed.Target, string.Join(", ", followed.StartedBy)))];

    /// <summary>Each object of any plugin the follow-up writes, with the placed records that started it.</summary>
    public static List<(RecordKey Object, string StartedBy)> WrittenOthers(FollowUpChecks checks) =>
        [.. checks.Written.Others.Select(other => (other.Outcome.Object.Key, string.Join(", ", other.StartedBy)))];

    public static void AssertNothingFollowed(FollowUpChecks checks)
    {
        Assert.Empty(checks.Written.Targets);
        Assert.Empty(checks.Written.Others);
        Assert.Empty(checks.CopiesLost);
        Assert.Empty(checks.HoldingUp);
    }

    /// <summary>
    /// The rock at <see cref="RockNow"/>, object 0; the crate, target 0; the second rock, object after the others; pebbles of the
    /// cleaned mod at the given places, the targets after the crate; the other objects in between, numbered from 1.
    /// </summary>
    public sealed record Scene
    {
        public List<Vector3> Pebbles { get; init; } = [];
        public List<OtherObject> Others { get; init; } = [];
        public Vector3 CrateAt { get; init; } = CrateNow;
        public Vector3 SecondRockAt { get; init; } = SecondRockNow;
        public ModKey SecondRockMod { get; init; } = BaseGameMod;
        public BaseKey SecondRockBase { get; init; } = Block.Base;

        /// <summary>Where the cleaned mod put a third rock, the fourth candidate, moved from 1000 units north; none when null.</summary>
        public Vector3? ThirdRockAt { get; init; }

        public ModKey ThirdRockMod { get; init; } = BaseGameMod;

        /// <summary>The pebbles whose removal restores them instead, to the given place, by their index among the pebbles.</summary>
        public Dictionary<int, Vector3> PebblesRestoredTo { get; init; } = [];

        public BaseKey RockBase { get; init; } = Block.Base;
        public OtherModsObjectsHandling Handling { get; init; } = OtherModsObjectsHandling.PullObjectToTheGround;
        public AlsoRemoveSettings AlsoRemove { get; init; } = TestPlacedGuardRules.DefaultFollowUp;

        /// <summary>The rock goes back only to its earlier turn instead of its earlier place.</summary>
        public bool RockTurnedBack { get; init; }

        /// <summary>The base the rock goes back to with its base and data, and nothing else unless <see cref="RockBackTo"/> is set; null when its position goes back.</summary>
        public BaseKey? RockBaseBack { get; init; }

        /// <summary>Where the rock goes back to, with <see cref="RockBaseBack"/> too when it is set; 1000 units north when null.</summary>
        public Vector3? RockBackTo { get; init; }

        /// <summary>The candidate whose only part going back is its enable state, which disables it; null for none.</summary>
        public int? HiddenAgain { get; init; }

        public IReadOnlyDictionary<OtherId, OtherObject?> TakenAway { get; init; } = NothingTakenAway;

        public ImmutableArray<TargetObject> Targets() =>
            [TestTargets.Create(CrateTarget, TestTargets.At(CrateAt), Block.Base, TestTargets.Space), .. Pebbles.Select((place, index) => PebbleTarget(index + 1, place))];

        public OtherObject Rock() => RockObject(0, RockNow) with { Base = RockBase };

        /// <summary>The rock leaving to its earlier place.</summary>
        public ObjectsLeaving RockLeaving() => new([new OtherLeaving(Rock(), Candidates()[RockCandidate].Earlier.Placement.Transform)], [], [], [], [], [], Land: null);

        public List<OtherObject> Objects() =>
        [
            .. (RockHidden ? Array.Empty<OtherObject>() : [Rock()]).Concat(Others)
                .Concat(SecondRockHidden ? Array.Empty<OtherObject>() : [RockObject(1, SecondRockAt, SecondRockMod) with { Base = SecondRockBase }])
                .Concat(ThirdRockAt is { } third ? [RockObject(2, third, ThirdRockMod)] : Array.Empty<OtherObject>())
                .Select((other, index) => other with { Id = new OtherId(index) }),
        ];

        /// <summary>The candidates: the rock, the crate, the second rock and the third one, each moved from 1000 units north unless set otherwise.</summary>
        public List<RecordChangedByCleanedMod> Candidates() =>
        [
            ShownBeforeOrNot(HiddenOrNot(RockCandidate, DataBackOrNot(RockChange())) with { BaseNow = RockBase }),
            HiddenOrNot(CrateCandidate, Moved(TestTargets.Key(CrateTarget), CrateAt) with { Target = new TargetId(CrateTarget) }),
            SecondRockChanged(FollowUpScenes.Rock(1, SecondRockAt, SecondRockMod)),
            .. ThirdRockAt is { } third ? [FollowUpScenes.Rock(2, third, ThirdRockMod)] : Array.Empty<RecordChangedByCleanedMod>(),
        ];

        /// <summary>The rock or the crate, left out of the candidates, as when its area does not let it go back; none when null.</summary>
        /// <remarks>The candidates after it come one index earlier among those whose areas let them go back.</remarks>
        public int? NoCandidate { get; init; }

        /// <summary>Whether the cleaned mod moved <see cref="NoCandidate"/>; when not, an unrelated mod moved it.</summary>
        public bool NoCandidateMovedByCleanedMod { get; init; } = true;

        /// <summary>The cleaned mod disabled the rock where it moved it, so the game does not show it; it was shown before.</summary>
        public bool RockHidden { get; init; }

        /// <summary>With <see cref="RockHidden"/>: the cleaned mod deleted the rock instead, so its winning version has no placement.</summary>
        public bool RockDeleted { get; init; }

        /// <summary>With <see cref="RockHidden"/>: a later mod moved the rock, so its position stays as that mod set it.</summary>
        public bool RockPositionKeptByLater { get; init; }

        /// <summary>The base the second rock goes back to with its base and data, besides its position; none when null.</summary>
        public BaseKey? SecondRockBaseBack { get; init; }

        /// <summary>The cleaned mod disabled the second rock where it moved it, so the game shows it again when it goes back.</summary>
        public bool SecondRockHidden { get; init; }

        /// <summary>The second rock goes back with its base and data, which carry a map marker, so the game no longer draws it.</summary>
        public bool SecondRockMarkerBack { get; init; }

        /// <summary>The candidates whose areas let them go back.</summary>
        public List<RecordChangedByCleanedMod> Cleared() => [.. Candidates().Where((_, index) => index != NoCandidate)];

        public CollectedObjects World() =>
            TestScenes.CreateWorld(Targets(), Objects()) with { ChangedRecords = [.. NoCandidateMovedByCleanedMod ? Candidates() : Cleared()] };

        public IRemovalDecisions Decisions() =>
            DecisionsOver(Targets(), [.. Targets().Select((target, index) => index > 0 && PebblesRestoredTo.TryGetValue(index - 1, out var place) ? place : (Vector3?)null)]);

        public IFollowUpOfObjectsLeaving Rule(int threads) => RuleOver(World(), threads, Support(Handling), AlsoRemove);

        /// <param name="goingBack">By their index among <see cref="Cleared"/>.</param>
        /// <param name="rule">The follow-up asked; the run's rule over the scene when null.</param>
        public FollowUpChecks Check(int[] goingBack, int[]? withClone = null, IFollowUpOfObjectsLeaving? rule = null)
        {
            var world = World();
            var candidates = Cleared();
            var going = goingBack.ToHashSet();
            return new FollowUpOfGoingBack(rule ?? RuleOver(world, threads: 1, Support(Handling), AlsoRemove), Decisions(), TakenAway, candidates, world, landUnder: (_, _) => null)
                .Check(going, (withClone ?? []).ToHashSet(), PassOf(world, candidates, going), land: null);
        }

        private RecordChangedByCleanedMod RockChange()
        {
            if (RockTurnedBack) return Turned(RockKey(0, BaseGameMod), RockNow);
            return RockBackTo is { } earlier
                ? Changed(RockKey(0, BaseGameMod), RockNow, new EulerPlacement(earlier, Vector3.Zero, 1f), PlacedParts.Position)
                : FollowUpScenes.Rock(0, RockNow);
        }

        private RecordChangedByCleanedMod DataBackOrNot(RecordChangedByCleanedMod change) =>
            RockBaseBack is { } earlierBase
                ? change with { Earlier = change.Earlier with { Base = earlierBase, Undo = RockBackTo is null ? PlacedParts.BaseAndData : change.Earlier.Undo | PlacedParts.BaseAndData } }
                : change;

        private RecordChangedByCleanedMod ShownBeforeOrNot(RecordChangedByCleanedMod change) =>
            RockHidden
                ? change with { Earlier = PositionKeptOrNot(change.Earlier with { Undo = change.Earlier.Undo | PlacedParts.Presence }), WhereItIsNow = RockDeleted ? null : change.WhereItIsNow }
                : change;

        private EarlierVersion PositionKeptOrNot(EarlierVersion earlier) =>
            RockPositionKeptByLater
                ? earlier with { Undo = earlier.Undo & ~PlacedParts.Position, LeftToLaterPlugins = earlier.LeftToLaterPlugins.Add(PlacedParts.Position, LaterMod.ToPluginName()) }
                : earlier;

        private RecordChangedByCleanedMod SecondRockChanged(RecordChangedByCleanedMod change)
        {
            var earlier = change.Earlier;
            if (SecondRockBaseBack is { } earlierBase) earlier = earlier with { Base = earlierBase, Undo = earlier.Undo | PlacedParts.BaseAndData };
            if (SecondRockHidden) earlier = earlier with { Undo = earlier.Undo | PlacedParts.Presence };
            if (SecondRockMarkerBack) earlier = earlier with { Reference = earlier.Reference with { HasMapMarker = true }, Undo = earlier.Undo | PlacedParts.BaseAndData };
            return change with { Earlier = earlier, BaseNow = SecondRockBase };
        }

        private RecordChangedByCleanedMod HiddenOrNot(int candidate, RecordChangedByCleanedMod change) =>
            HiddenAgain == candidate
                ? change with { Earlier = change.Earlier with { Undo = PlacedParts.Presence }, PresenceBefore = new PlacedPresence(Removed: true, Parent: null, OppositeOfParent: false) }
                : change;
    }

    public static readonly IReadOnlyDictionary<OtherId, OtherObject?> NothingTakenAway = new Dictionary<OtherId, OtherObject?>();

    public static OtherModsObjectsSettings Support(OtherModsObjectsHandling handling = OtherModsObjectsHandling.PullObjectToTheGround) =>
        new(handling, TouchGap: 1f, SupportLostFraction: 0.5f);

    public static WorldStanding Standing(IRemovalDecisions final) => new(final, NothingTakenAway);

    /// <summary>Whether a placed record ends removed with the given candidates going back and none removed by the run.</summary>
    public static PresenceInPass PassOf(CollectedObjects world, List<RecordChangedByCleanedMod> candidates, HashSet<int> goingBack) =>
        new(FinalPresence.Of(world, new HashSet<RecordKey>(), candidates), goingBack.Contains);

    /// <summary>The run's decisions over the target objects: none decided, none protected.</summary>
    /// <param name="restoredTo">By target: where its removal restores it to instead of disabling it; null to disable it.</param>
    public static IRemovalDecisions DecisionsOver(ImmutableArray<TargetObject> targets, ImmutableArray<Vector3?> restoredTo = default) =>
        RemovalDecisions.Start(
            ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved),
            [
                .. targets.Select((target, index) => !restoredTo.IsDefault && restoredTo[index] is { } place
                    ? new WhenRemoved.Restore(TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(place, Vector3.Zero, 1f), target.Base))
                    : (WhenRemoved)new WhenRemoved.Disable()),
            ]);

    public static IFollowUpOfObjectsLeaving RuleOver(CollectedObjects world, int threads, OtherModsObjectsSettings support, AlsoRemoveSettings? alsoRemove = null) =>
        TestPlacedGuardRules.Create(Shapes, Trust, Plugins, support, TestGround.FlatAtZero(), threads).FollowUpOver(world, alsoRemove ?? TestPlacedGuardRules.DefaultFollowUp);

    /// <summary>The crate restored to its earlier version on a copy of <paramref name="final"/>.</summary>
    public static IRemovalDecisions GoingBack(IRemovalDecisions final, Scene scene) =>
        final.WorkingCopy().RecordGoingBack([new GoingBack(new TargetId(CrateTarget), new GoingBackWhereRemoved(), scene.Candidates()[CrateCandidate].Earlier)]);

    public static TargetObject PebbleTarget(int index, Vector3 place) => TestTargets.Create(index, TestTargets.At(place), Pebble.Base, TestTargets.Space);

    public static OtherObject Placed(ModKey mod, int index, TestStatic model, Vector3 position) => TestShapes.Placed(mod, index, model.Base, position);

    /// <summary>A rock where the cleaned mod put it.</summary>
    public static OtherObject RockObject(int index, Vector3 now, ModKey? mod = null) =>
        TestShapes.Placed(mod ?? BaseGameMod, index, Block.Base, now) with { ChangeWithoutCleanedMod = new ObjectChange(TestTargets.TargetName, (mod ?? BaseGameMod).ToPluginName()) };

    public static RecordKey RockKey(int index, ModKey mod) => new FormKey(mod, 0x900 + (uint)index).ToRecordKey();

    public static RecordChangedByCleanedMod Rock(int index, Vector3 now, ModKey? mod = null) => Moved(RockKey(index, mod ?? BaseGameMod), now);

    /// <summary>A block the cleaned mod moved to <paramref name="now"/> from 1000 units north, whose position goes back.</summary>
    public static RecordChangedByCleanedMod Moved(RecordKey key, Vector3 now) => Changed(key, now, new EulerPlacement(now + new Vector3(0, 1000, 0), Vector3.Zero, 1f), PlacedParts.Position);

    /// <summary>A block the cleaned mod turned about its vertical axis at <paramref name="now"/>, whose rotation goes back.</summary>
    public static RecordChangedByCleanedMod Turned(RecordKey key, Vector3 now) => Changed(key, now, new EulerPlacement(now, new Vector3(0, 0, 0.05f), 1f), PlacedParts.Rotation);

    public static RecordChangedByCleanedMod Changed(RecordKey key, Vector3 now, EulerPlacement earlier, PlacedParts undo) =>
        new(
            key,
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            WinningModPastPatches: TestTargets.TargetName,
            new EulerPlacement(now, Vector3.Zero, 1f),
            Block.Base,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, earlier, Block.Base) with { Undo = undo },
            Target: null,
            LinkedFromCleanedMod: false,
            PresenceBefore: default);
}
