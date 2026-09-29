using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

public class LeftoverDecisionTests
{
    private static readonly DirectionSector[] Sectors = Enum.GetValues<DirectionSector>();
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");

    private static readonly TestStatic Room = new(
        new FormKey(OtherMod, 0x801), @"test\room.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100), new Vector3(100))));

    private static readonly ShapeCatalog Shapes = TestShapes.Create(OtherMod, "LeftoverDecisionData", Room);

    private static LeftoverOptions Config(int occupiedPercent = 50, int removedPercent = 60, params InvisibleObjectKind[] protectedKinds) => new(
        LookAround: 1024,
        DirectionClearedPercent: 50,
        ClearedDirectionsPercent: removedPercent,
        OccupiedDirectionsPercent: occupiedPercent,
        NeverRemove: protectedKinds.ToHashSet(),
        Preset: ProtectedInvisibleObjectsPreset.Custom);

    [Theory]
    [InlineData(3, 3, nameof(LeftoverDecision.KeptTooFewSurroundingObjects))]
    [InlineData(4, 3, nameof(LeftoverDecision.RemovedSurroundingsRemoved))]
    [InlineData(4, 2, nameof(LeftoverDecision.KeptSurroundingsMostlyKept))]
    [InlineData(8, 5, nameof(LeftoverDecision.RemovedSurroundingsRemoved))]
    [InlineData(8, 4, nameof(LeftoverDecision.KeptSurroundingsMostlyKept))]
    public void DirectionRuleNeedsEnoughOccupiedAndRemovedDirections(int occupied, int removed, string expected) =>
        Assert.Equal(
            Enum.Parse<LeftoverDecision>(expected),
            LeftoverInvisibleObjectSelector.DecideByDirections(Areas(occupied, removed), Config()));

    [Fact]
    public void NoSurroundingsKeepsTheObject() =>
        Assert.Equal(
            LeftoverDecision.KeptTooFewSurroundingObjects,
            LeftoverInvisibleObjectSelector.DecideByDirections(new SectorAreas(50), Config(occupiedPercent: 10)));

    [Fact]
    public void FullRemovalRequirementNeedsEveryOccupiedDirection()
    {
        Assert.Equal(
            LeftoverDecision.KeptSurroundingsMostlyKept,
            LeftoverInvisibleObjectSelector.DecideByDirections(Areas(8, 7), Config(removedPercent: 100)));
        Assert.Equal(
            LeftoverDecision.RemovedSurroundingsRemoved,
            LeftoverInvisibleObjectSelector.DecideByDirections(Areas(8, 8), Config(removedPercent: 100)));
    }

    [Fact]
    public void KeepRulesOverrideOnlyRemovals()
    {
        const int protectedLightInRoom = 0;
        const int referencedMarkerInRoom = 1;
        const int markerInRoom = 2;
        const int protectedLightAlone = 3;
        var inRoom = Vector3.Zero;
        var targets = new List<TargetObject>
        {
            TestTargets.Create(protectedLightInRoom, TestTargets.At(inRoom)),
            TestTargets.Create(referencedMarkerInRoom, TestTargets.At(inRoom)),
            TestTargets.Create(markerInRoom, TestTargets.At(inRoom)),
            TestTargets.Create(protectedLightAlone, TestTargets.At(new Vector3(5000, 0, 0))),
        };
        var looks = new TargetLooks(
        [
            ObjectVisibility.Invisible(InvisibleObjectKind.Lights),
            ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers),
            ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers),
            ObjectVisibility.Invisible(InvisibleObjectKind.Lights),
        ]);
        var protection = Protection.Build(
            targets,
            [],
            TestTargets.References(
                targets.Count,
                new Dictionary<int, KeepReason> { [referencedMarkerInRoom] = new(KeepKind.NonPlacedReference, "QUST record", "linked from QUST") }));
        var room = TestShapes.Placed(OtherMod, 0, Room.Ref, inRoom);

        var evaluated = TestLeftovers.CreateSelector(targets, looks, Shapes, [room], Config(protectedKinds: InvisibleObjectKind.Lights))
            .SelectRemovals(new HashSet<int>(), new Execution(Environment.ProcessorCount));
        var result = TestLeftovers.DecideWithLedger(evaluated, protection, targets.Count);

        var decisions = result.Evaluations.ToDictionary(evaluation => evaluation.TargetIndex);
        Assert.Equal(LeftoverDecision.KeptProtectedType, decisions[protectedLightInRoom].Decision);
        Assert.Equal(LeftoverDecision.KeptReferenced, decisions[referencedMarkerInRoom].Decision);
        Assert.Equal(KeepKind.NonPlacedReference, decisions[referencedMarkerInRoom].KeepReason?.Kind);
        Assert.Equal(LeftoverDecision.RemovedInsideOtherObject, decisions[markerInRoom].Decision);
        Assert.Null(decisions[markerInRoom].KeepReason);
        Assert.Equal(LeftoverDecision.KeptTooFewSurroundingObjects, decisions[protectedLightAlone].Decision);
        Assert.Equal(markerInRoom, Assert.Single(result.Evaluations, evaluation => evaluation.IsRemoved).TargetIndex);
    }

    /// <summary>The first <paramref name="removed"/> of <paramref name="occupied"/> directions fully removed, the rest fully kept.</summary>
    private static SectorAreas Areas(int occupied, int removed)
    {
        var areas = new SectorAreas(50);
        for (var i = 0; i < occupied; i++) areas.Add(Sectors[i], 100, removed: i < removed);
        return areas;
    }
}
