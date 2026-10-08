using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

public class LeftBehindOutcomeTests
{
    private static readonly DirectionSector[] Sectors = Enum.GetValues<DirectionSector>();
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");

    private static readonly TestStatic Room = new(
        new FormKey(OtherMod, 0x801), @"test\room.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100), new Vector3(100))));

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(OtherMod, "LeftBehindOutcomeData", Room));

    private static LeftBehindOptions Config(int occupiedPercent = 50, int removedPercent = 60, params InvisibleObjectKind[] protectedKinds) => new(
        LookAround: 1024,
        DirectionClearedPercent: 50,
        ClearedDirectionsPercent: removedPercent,
        OccupiedDirectionsPercent: occupiedPercent,
        NeverRemove: protectedKinds.ToHashSet(),
        Preset: ProtectedInvisibleObjectsPreset.Custom);

    [Theory]
    [InlineData(3, 3, nameof(LeftBehindOutcome.KeptTooFewSurroundingObjects))]
    [InlineData(4, 3, nameof(LeftBehindOutcome.RemovedSurroundingsRemoved))]
    [InlineData(4, 2, nameof(LeftBehindOutcome.KeptSurroundingsMostlyKept))]
    [InlineData(8, 5, nameof(LeftBehindOutcome.RemovedSurroundingsRemoved))]
    [InlineData(8, 4, nameof(LeftBehindOutcome.KeptSurroundingsMostlyKept))]
    public void DirectionRuleNeedsEnoughOccupiedAndRemovedDirections(int occupied, int removed, string expected) =>
        Assert.Equal(
            Enum.Parse<LeftBehindOutcome>(expected),
            LeftBehindRule.DecideByDirections(Areas(occupied, removed), Config()));

    [Fact]
    public void NoSurroundingsKeepsTheObject() =>
        Assert.Equal(
            LeftBehindOutcome.KeptTooFewSurroundingObjects,
            LeftBehindRule.DecideByDirections(new SectorAreaTally(50).Build(),Config(occupiedPercent: 10)));

    [Fact]
    public void FullRemovalRequirementNeedsEveryOccupiedDirection()
    {
        Assert.Equal(
            LeftBehindOutcome.KeptSurroundingsMostlyKept,
            LeftBehindRule.DecideByDirections(Areas(8, 7), Config(removedPercent: 100)));
        Assert.Equal(
            LeftBehindOutcome.RemovedSurroundingsRemoved,
            LeftBehindRule.DecideByDirections(Areas(8, 8), Config(removedPercent: 100)));
    }

    [Fact]
    public void KeepRulesOverrideOnlyRemovals()
    {
        const int protectedLightInRoom = 0;
        const int referencedMarkerInRoom = 1;
        const int markerInRoom = 2;
        const int protectedLightAlone = 3;
        var inRoom = Vector3.Zero;
        var light = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.Lights));
        var xMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        var targets = new List<TargetObject>
        {
            TestTargets.Create(protectedLightInRoom, TestTargets.At(inRoom), light, TestTargets.Space),
            TestTargets.Create(referencedMarkerInRoom, TestTargets.At(inRoom), xMarker, TestTargets.Space),
            TestTargets.Create(markerInRoom, TestTargets.At(inRoom), xMarker, TestTargets.Space),
            TestTargets.Create(protectedLightAlone, TestTargets.At(new Vector3(5000, 0, 0)), light, TestTargets.Space),
        };
        var protection = ObjectsToKeep.Build(
            targets,
            [],
            TestTargets.References(
                targets.Count,
                new Dictionary<int, KeepReason> { [referencedMarkerInRoom] = TestKeepReasons.Quest }));
        var room = TestShapes.Placed(OtherMod, 0, Room.Base, inRoom);

        var evaluated = TestLeftBehind.CreateRule(targets, Shapes, [room], Config(protectedKinds: InvisibleObjectKind.Lights))
            .SelectRemovals(new HashSet<int>(), new Execution(Environment.ProcessorCount));
        var result = TestLeftBehind.DecideWithRemovalDecisions(evaluated, protection, targets.Count);

        var decisions = PerIndexTable<LeftBehindCheck>.From(result.Evaluations, evaluation => evaluation.TargetIndex);
        Assert.Equal(LeftBehindOutcome.KeptProtectedType, decisions.Get(protectedLightInRoom).Decision);
        Assert.Equal(LeftBehindOutcome.KeptReferenced, decisions.Get(referencedMarkerInRoom).Decision);
        Assert.Equal(KeepKind.NonPlacedReference, decisions.Get(referencedMarkerInRoom).KeepReason?.Kind);
        Assert.Equal(LeftBehindOutcome.RemovedInsideOtherObject, decisions.Get(markerInRoom).Decision);
        Assert.Null(decisions.Get(markerInRoom).KeepReason);
        Assert.Equal(LeftBehindOutcome.KeptTooFewSurroundingObjects, decisions.Get(protectedLightAlone).Decision);
        Assert.Equal(markerInRoom, Assert.Single(result.Evaluations, evaluation => evaluation.IsRemoved).TargetIndex);
    }

    /// <summary>The first <paramref name="removed"/> of <paramref name="occupied"/> directions fully removed, the rest fully kept.</summary>
    private static SectorAreas Areas(int occupied, int removed)
    {
        var tally = new SectorAreaTally(50);
        for (var i = 0; i < occupied; i++) tally.Add(Sectors[i], 100, removed: i < removed);
        return tally.Build();
    }
}
