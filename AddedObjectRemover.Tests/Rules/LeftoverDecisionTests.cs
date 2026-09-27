using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

public class LeftoverDecisionTests
{
    private static readonly DirectionSector[] Sectors = Enum.GetValues<DirectionSector>();

    private static LeftoverConfig Config(int occupiedPercent = 50, int removedPercent = 60, params InvisibleObjectKind[] protectedKinds) => new(
        Enabled: true,
        SearchRadius: 1024,
        DirectionThresholdPercent: 50,
        RemovedDirectionsPercent: removedPercent,
        OccupiedDirectionsPercent: occupiedPercent,
        ProtectedPreset: ProtectedInvisibleObjectsPreset.Custom,
        ProtectedKinds: protectedKinds.ToHashSet(),
        MovesKeptMarkers: false);

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
        var targets = TestTargets.CreateMany(2);
        var keepRule = new KeepReferencedRule(
            targets,
            new Dictionary<FormKey, KeepReason> { [TestTargets.Key(1)] = new(KeepKind.NonPlacedReference, "QUST record", "linked from QUST") },
            LinkedGroups.Build(targets, []));
        var config = Config(protectedKinds: InvisibleObjectKind.Lights);

        Assert.Equal(
            (LeftoverDecision.KeptSurroundingsMostlyKept, (KeepReason?)null),
            LeftoverInvisibleObjectSelector.ApplyKeepRules(1, InvisibleObjectKind.Lights, LeftoverDecision.KeptSurroundingsMostlyKept, config, keepRule));
        Assert.Equal(
            LeftoverDecision.KeptProtectedType,
            LeftoverInvisibleObjectSelector.ApplyKeepRules(0, InvisibleObjectKind.Lights, LeftoverDecision.RemovedInsideOtherObject, config, keepRule).Decision);

        var referenced = LeftoverInvisibleObjectSelector.ApplyKeepRules(1, InvisibleObjectKind.XMarkers, LeftoverDecision.RemovedSurroundingsRemoved, config, keepRule);
        Assert.Equal(LeftoverDecision.KeptReferenced, referenced.Decision);
        Assert.Equal(KeepKind.NonPlacedReference, referenced.KeepReason?.Kind);

        Assert.Equal(
            (LeftoverDecision.RemovedSurroundingsRemoved, (KeepReason?)null),
            LeftoverInvisibleObjectSelector.ApplyKeepRules(0, InvisibleObjectKind.XMarkers, LeftoverDecision.RemovedSurroundingsRemoved, config, keepRule));
    }

    /// <summary>The first <paramref name="removed"/> of <paramref name="occupied"/> directions fully removed, the rest fully kept.</summary>
    private static SectorAreas Areas(int occupied, int removed)
    {
        var areas = new SectorAreas(50);
        for (var i = 0; i < occupied; i++) areas.Add(Sectors[i], 100, removed: i < removed);
        return areas;
    }
}
