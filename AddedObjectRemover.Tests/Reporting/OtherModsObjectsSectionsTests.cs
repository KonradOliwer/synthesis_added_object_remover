using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The log lines and the report file of what became of other mods' objects left without support.</summary>
public class OtherModsObjectsSectionsTests
{
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly ReportContext Normal = new(TestBaseFacts.Unresolved, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(TestBaseFacts.Unresolved, null!, Detailed: true);
    private static readonly CollectedObjects World = TestScenes.CreateWorld([], []) with
    {
        Spaces = new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Room", SpaceKind.Interior, "Room") },
    };

    private static readonly OtherModsObjectsResult Settled = new(
        StepOutcome.Performed,
        [
            new OtherModsObjectOutcome(Placed(0, new Vector3(1, 2, 40)), OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 40f, ReferenceRoles.None),
            new OtherModsObjectOutcome(Placed(1, new Vector3(3, 4, 50)), OtherModsObjectFate.PulledDown, LostShare: 0.75f, Drop: 8f, ReferenceRoles.None),
            new OtherModsObjectOutcome(
                Placed(2, new Vector3(5, 6, 60)), OtherModsObjectFate.LeftNothingBelowNotRemovable, LostShare: 1f, Drop: 0f, ReferenceRoles.Persistent | ReferenceRoles.HasEnableParent),
        ],
        Rounds: [],
        ProposedRounds: [],
        Failure: null);

    [Fact]
    public void TheSummaryCountsEachFateThereIsAndOnlyTheDetailedLogListsTheObjects()
    {
        var normal = LogSections.BuildLeftWithoutSupportSection(World, Settled, Normal).Lines.ToList();
        var detailed = LogSections.BuildLeftWithoutSupportSection(World, Settled, Detailed).Lines.ToList();

        Assert.Equal(
            ["Other mods' objects left without support: 3; pulled down: 2; left where they stand, nothing below within 2048 units and not removable: 1."],
            normal);
        Assert.Equal(normal[0], detailed[0]);
        Assert.Equal(4, detailed.Count);
        Assert.EndsWith(": pulled down by 40.0 units; 100 % of its support gone.", detailed[1]);
        Assert.EndsWith("at (5, 6, 60): left where it stands, nothing below within 2048 units and not removable (persistent, has an enable parent); 100 % of its support gone.", detailed[3]);
    }

    [Fact]
    public void WithoutAnObjectOnlyTheDetailedStepTimeIsPrintedAndTheStoppedStepHasItsNotPerformedLine()
    {
        var stopped = new OtherModsObjectsResult(StepOutcome.StoppedByUnexpectedError, [], [], [], new PartFailure(OtherModsObjectsParts.LeftWithoutSupport, "broke", Subject: null));
        var none = new OtherModsObjectsResult(StepOutcome.Performed, [], [], [], Failure: null);
        var decisions = RemovalDecisions.Start(ObjectsToKeep.Build([], [], [], TestBaseFacts.Unresolved), 0);

        Assert.Empty(LogSections.BuildLeftWithoutSupportSection(World, OtherModsObjectsResult.SwitchedOff, Detailed).Lines);
        Assert.Empty(LogSections.BuildLeftWithoutSupportStepSection(World, none, decisions, TestTargets.TargetName, TimeSpan.Zero, Normal).Lines);
        Assert.Equal(
            ["Step time: Other mods' objects left without support in 0.0s."],
            LogSections.BuildLeftWithoutSupportStepSection(World, none, decisions, TestTargets.TargetName, TimeSpan.Zero, Detailed).Lines.ToList());
        Assert.Equal(
            ["Other mods' objects left without support: not performed because of an unexpected error."],
            LogSections.BuildLeftWithoutSupportStepSection(World, stopped, decisions, TestTargets.TargetName, TimeSpan.Zero, Normal).Lines.ToList());
    }

    [Fact]
    public void TheDetailedStepLineCountsTheCleanedModsObjectsRemovedWithTheObjectsTakenAway()
    {
        var targets = TestTargets.CreateMany(2);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(2), TestBaseFacts.Unresolved);
        var decisions = RemovalDecisions.Start(protection, 2)
            .Apply(RoundKind.TooClose, [new ProposedRemoval(new TargetId(0), new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize()))])
            .Apply(RoundKind.AlsoRemove, [new ProposedRemoval(new TargetId(1), new RemovalReason.TouchingAnotherModsObject(new OtherId(0)))]);
        var settled = Settled with { Rounds = [decisions.Rounds[^1]] };

        var lines = LogSections.BuildLeftWithoutSupportStepSection(World with { Targets = [.. targets] }, settled, decisions, TestTargets.TargetName, TimeSpan.Zero, Detailed).Lines;

        Assert.StartsWith($"Other mods' objects left without support: 3 found; objects of {TestTargets.TargetName} removed with them: 1 in ", Assert.Single(lines));
    }

    [Fact]
    public void ARemovalWithAnotherModsObjectTakenAwayNamesThatObject()
    {
        var other = Placed(0, new Vector3(1, 2, 3)) with { EditorId = "Lantern" };
        var world = TestScenes.CreateWorld(TestTargets.CreateMany(1), [other]);

        Assert.Equal(
            "touches Lantern [000900:Other.esp], another mod's object left without support",
            Describe.RemovalReason(world, new TouchingAnotherModsObjectRemoval(0, other)));
        Assert.Equal(
            "60 % of its support was removed or pulled down (mostly Lantern [000900:Other.esp], another mod's object left without support)",
            Describe.RemovalReason(world, new SupportWithAnotherModsObjectRemoval(0, 0.6f, other)));
    }

    [Fact]
    public void TheReportFileHasOneRowPerObjectWithTheNewHeightOnlyForThePulledOnes()
    {
        var table = Tables.BuildLeftWithoutSupportTable(World, Settled, Detailed)!;

        Assert.Equal(ReportFileNames.LeftWithoutSupportFileName, table.FileName);
        Assert.Equal(3, table.Rows.Length);
        Assert.Equal(["pulled down", "Other.esp", "Room", "1", "2", "40", "0", "1", ""], WithoutKeyAndBase(table.Rows[0]));
        Assert.Equal(
            ["\"left where it stands, nothing below within 2048 units and not removable\"", "Other.esp", "Room", "5", "6", "60", "", "1", "\"persistent, has an enable parent\""],
            WithoutKeyAndBase(table.Rows[2]));
    }

    [Fact]
    public void ThereIsNoReportFileWithoutAnObject() =>
        Assert.Null(Tables.BuildLeftWithoutSupportTable(World, OtherModsObjectsResult.SwitchedOff, Detailed));

    private static OtherObject Placed(int id, Vector3 position) => TestShapes.Placed(OtherMod, id, default, position);

    /// <summary>The columns but the form key, the Editor ID and the base, which other tables check.</summary>
    private static string[] WithoutKeyAndBase(IReadOnlyList<string> row) => [row[0], .. row.Skip(4)];
}
