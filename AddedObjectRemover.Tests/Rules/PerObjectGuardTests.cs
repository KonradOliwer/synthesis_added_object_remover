using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// An unexpected error while one object is handled is reported for that object only: the check of the target objects (too close, keep)
/// and the selection of other mods' objects go on with all the others, whatever the thread count.
/// </summary>
public class PerObjectGuardTests
{
    private const string FailureMessage = "bad triangle data";
    private const float Multiplier = 1.5f;
    private const float Spacing = 1000f;
    private const int Healthy = 0;
    private const int Faulty = 1;
    private const int AlsoHealthy = 2;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Guarded.esp");

    private static readonly TestStatic Table = new(
        new FormKey(Mod, 0x701), @"test\guard_table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic Boulder = new(
        new FormKey(Mod, 0x703), @"test\guard_boulder.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-50, -50, -20), new Vector3(50, 50, 40))));

    private static readonly IBaseObjectShapes Shapes = TestShapes.Create(Mod, "PerObjectGuardData", Table, Boulder);

    /// <summary>Wraps real shapes; asking whether an object of the faulty base is visible fails, as a broken record would.</summary>
    private sealed class FaultyBaseShapes(IBaseObjectShapes inner, BaseKey faultyBase) : IBaseObjectShapes
    {
        public BaseShape Of(BaseKey? baseKey) => inner.Of(baseKey);

        public ObjectVisibility VisibilityOf(BaseKey? baseKey, bool isPrimitive, bool hasMapMarker) =>
            baseKey == faultyBase ? throw new InvalidDataException(FailureMessage) : inner.VisibilityOf(baseKey, isPrimitive, hasMapMarker);

        public IReadOnlyList<MeasuredBase> Computed() => inner.Computed();
    }

    private static IBaseObjectShapes FaultyWhenAskedAbout(BaseKey faultyBase)
    {
        var shapes = new FaultyBaseShapes(Shapes, faultyBase);
        TestShapes.ShareMeshFiles(Shapes, shapes);
        return shapes;
    }

    /// <summary>Three target tables, each with an other-mod table on top of it; the middle target uses another base.</summary>
    private static (List<TargetObject> Targets, List<OtherObject> OtherModObjects) CreateScene(BaseKey middleTargetBase, BaseKey middleOtherBase)
    {
        BaseKey[] targetBases = [Table.Base, middleTargetBase, Table.Base];
        BaseKey[] otherBases = [Table.Base, middleOtherBase, Table.Base];
        var targets = Enumerable.Range(0, targetBases.Length)
            .Select(i => TestTargets.Create(i, TestTargets.At(Position(i)), targetBases[i], TestTargets.Space))
            .ToList();
        var others = Enumerable.Range(0, otherBases.Length).Select(i => TestShapes.Placed(Mod, i, otherBases[i], Position(i))).ToList();
        return (targets, others);
    }

    private static Vector3 Position(int index) => new(index * Spacing, 0, 0);

    private static TooCloseResult FindTooClose(
        List<TargetObject> targets, ObjectCaches world, IBaseObjectShapes shapes, int threads, Replacements replacements) =>
        TooCloseObjects.Find(
            new TooCloseInput(
                [.. targets],
                world.VisibleTargets,
                TargetWorkOrder.Of(targets),
                world.ObjectsThatCanCauseRemovals(replacements, NpcHandling.Ignore),
                Npcs: null,
                shapes,
                new TriangleStore(shapes.ReadTriangles),
                new Execution(threads)),
            new TooCloseOptions(Multiplier, ZoneShape.BoundingBox, NpcHandling.Ignore));

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void ATargetWhoseTooCloseCheckFailsIsNotRemovedAndTheOthersAreStillChecked(int threads)
    {
        var (targets, others) = CreateScene(middleTargetBase: Boulder.Base, middleOtherBase: Table.Base);
        var world = TestScenes.Create(targets, others, Shapes, threads: threads);
        var replacements = Replacements.None(others.Count);

        var result = FindTooClose(targets, world, FaultyWhenAskedAbout(Boulder.Base), threads, replacements);

        Assert.Equal([Healthy, AlsoHealthy], result.Hits.Select(hit => hit.TargetIndex));
        Assert.Equal([Healthy, AlsoHealthy], result.Proposals.Select(proposal => proposal.Target.Index));
        Assert.Equal(new TargetFailure(Faulty, "InvalidDataException: " + FailureMessage), Assert.Single(result.Failures));
    }

    [Fact]
    public void ARunWithoutFailuresHasNoTooCloseFailures()
    {
        var (targets, others) = CreateScene(middleTargetBase: Table.Base, middleOtherBase: Table.Base);
        var world = TestScenes.Create(targets, others, Shapes);

        var result = FindTooClose(targets, world, Shapes, threads: 1, Replacements.None(others.Count));

        Assert.Equal([Healthy, Faulty, AlsoHealthy], result.Hits.Select(hit => hit.TargetIndex));
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AnOtherModObjectThatCannotBeMeasuredIsReportedAndCausesNoRemovals(int threads)
    {
        var (targets, others) = CreateScene(middleTargetBase: Table.Base, middleOtherBase: Boulder.Base);
        var reported = new System.Collections.Concurrent.ConcurrentBag<UnexpectedError>();
        var shapes = FaultyWhenAskedAbout(Boulder.Base);
        var world = TestScenes.Create(targets, others, shapes, threads: threads, reportUnexpected: reported.Add);

        var result = FindTooClose(targets, world, shapes, threads, Replacements.None(others.Count));

        Assert.Equal([Healthy, AlsoHealthy], result.Hits.Select(hit => hit.TargetIndex));
        Assert.Empty(result.Failures);
        var error = Assert.Single(reported.Distinct());
        Assert.Equal("selecting the objects that can cause removals", error.Part);
        Assert.Equal("InvalidDataException: " + FailureMessage, error.Failure);
        Assert.Equal("They were not used to remove target objects.", error.Consequence);
        Assert.Equal(new ErrorSubject(SubjectKind.Object, others[Faulty].Key.ToString(), Position: Faulty), error.Subject);
    }

    [Fact]
    public void ATargetWhoseKeepCheckFailsIsKeptAndTheOthersAreChecked()
    {
        var targets = TestTargets.CreateMany(3);
        var reasons = new FaultyReferences(TestTargets.References(3), Faulty);

        var protection = ObjectsToKeep.Build(targets, [], reasons);

        Assert.Equal(new TargetFailure(Faulty, "InvalidDataException: " + FailureMessage), Assert.Single(protection.CheckFailures));
        Assert.True(protection.TryGetKeepReason(Faulty, out var reason));
        Assert.Equal(KeepKind.CheckFailed, reason.Kind);
        Assert.False(protection.IsProtected(Healthy));
        Assert.False(protection.IsProtected(AlsoHealthy));
    }

    [Fact]
    public void AKeepCheckThatFailsKeepsTheWholeLinkedGroup()
    {
        var targets = TestTargets.CreateMany(3);
        var reasons = new FaultyReferences(TestTargets.References(3), Faulty);

        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(Healthy, Faulty)], reasons);

        Assert.True(protection.IsProtected(Healthy));
        Assert.True(protection.IsProtected(Faulty));
        Assert.False(protection.IsProtected(AlsoHealthy));
    }

    [Fact]
    public void TouchExplanationsAreQuietlySkippedWhenTheTouchChainsAreMissing()
    {
        var (targets, _) = CreateScene(middleTargetBase: Table.Base, middleOtherBase: Table.Base);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Count));
        var seeded = TestSeededDecisions.Seed(protection, targets.Count, [Healthy]);
        var triangles = new TriangleStore(Shapes.ReadTriangles);
        var input = new RestingObjectsInput(
            [.. targets], protection, ObjectsOfAnyPlugin: null, Shapes, triangles, TestGround.NoTerrain(), new Execution(1));
        var restingObjects = TestRestingObjects.Run(seeded, input, new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouching, 1f, SupportLostFraction: 1f));

        var details = ExplanationFinder.Compute(
            new ReportFileDetailsInput(restingObjects, TouchChains: null, [.. targets], Shapes, triangles, Bases: null!, new Execution(1)));

        Assert.True(restingObjects.Result.HadSeeds);
        Assert.Null(details.Touch);
        Assert.Null(details.MeshOrigins);
    }

    /// <summary>Reasons by target index; reading the faulty target's fails.</summary>
    private sealed class FaultyReferences(IReadOnlyList<KeepReason?> inner, int faultyIndex) : IReadOnlyList<KeepReason?>
    {
        public int Count => inner.Count;

        public KeepReason? this[int index] => index == faultyIndex ? throw new InvalidDataException(FailureMessage) : inner[index];

        public IEnumerator<KeepReason?> GetEnumerator() => inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
