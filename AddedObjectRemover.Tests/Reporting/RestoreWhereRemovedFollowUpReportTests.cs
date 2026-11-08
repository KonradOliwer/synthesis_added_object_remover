using System.Numerics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

using static RestoreWhereRemovedReportScene;

/// <summary>The follow-up of what went back, as the log and the report files of the restore step name it, over results built by hand.</summary>
public sealed class RestoreWhereRemovedFollowUpReportTests
{
    /// <summary>
    /// The follow-up of a rock going back removes an object the cleaned mod added on it and pulls down a lantern of the base game: the log counts
    /// both, the detailed log lists each with the rock, and the follow-up report file has a row for each.
    /// </summary>
    [Fact]
    public void TheFollowUpIsCountedListedAndReported()
    {
        var lantern = TestShapes.Placed(Mutagen.Bethesda.Plugins.ModKey.FromNameAndExtension("Base.esm"), 7, new BaseKey(Key(0x300), BaseLinkKind.PlaceableObject), new Vector3(1, 2, 50)) with { EditorId = "Lantern" };
        var world = World() with { OtherModObjects = [lantern] };
        var rock = Rock(0x913, PlacedParts.Position);
        var result = ObjectsOnly(Restored(rock, Cleared)) with
        {
            FollowUp = new RestoreFollowUp(
                [new FollowUpTarget(new TargetId(0), RestoredTo: null, [rock.Key])],
                [new FollowUpOtherObject(new OtherModsObjectOutcome(lantern, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 40f, ReferenceRoles.None), [rock.Key])]),
        };

        var normal = LogSections.BuildRestoreWhereRemovedSection(world, result, Elapsed, Normal).Lines;
        var detailed = LogSections.BuildRestoreWhereRemovedSection(world, result, Elapsed, Detailed with { Bases = TestBaseFacts.Unresolved }).Lines;
        var table = Assert.Single(Tables.BuildRestoredWhereRemovedTables(world, result, [], Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedFollowUpFileName);

        Assert.Contains("Follow-up of what went back: 1 object the cleaned mod added or moved removed, 1 object of a trusted mod pulled down.", normal);
        var startedBy = $", with {RecordLabels.Of(rock.Key, null)} going back";
        Assert.Contains(detailed, line => line.StartsWith("  Follow-up of what went back: ", StringComparison.Ordinal) && line.EndsWith($": removed{startedBy}", StringComparison.Ordinal));
        Assert.Contains(detailed, line => line.StartsWith($"  Follow-up of what went back: Lantern [{lantern.Key}]", StringComparison.Ordinal) && line.EndsWith($"pulled down by 40.0 units{startedBy}", StringComparison.Ordinal));
        Assert.Equal(["object the cleaned mod added or moved", "trusted mod's object"], table.Rows.Select(row => row[0].Trim('"')));
        Assert.Equal(["removed", "pulled down"], table.Rows.Select(row => row[7].Trim('"')));
        Assert.All(table.Rows, row => Assert.Equal(rock.Key.ToString(), row[9].Trim('"')));
    }

    /// <summary>
    /// The ground going back down pulls down one lantern of the base game alone, and another together with a rock going back: the
    /// detailed log names the ground after the rocks, and the follow-up report file appends "ground" to the keys.
    /// </summary>
    [Fact]
    public void AFollowUpStartedByTheGroundNamesTheGroundInTheLogAndTheReportFile()
    {
        var baseGame = Mutagen.Bethesda.Plugins.ModKey.FromNameAndExtension("Base.esm");
        var alone = TestShapes.Placed(baseGame, 7, new BaseKey(Key(0x300), BaseLinkKind.PlaceableObject), new Vector3(1, 2, 50)) with { EditorId = "Lantern" };
        var withRock = TestShapes.Placed(baseGame, 8, new BaseKey(Key(0x300), BaseLinkKind.PlaceableObject), new Vector3(3, 4, 50)) with { EditorId = "Lamp" };
        var world = World() with { OtherModObjects = [alone, withRock] };
        var rock = Rock(0x913, PlacedParts.Position);
        static OtherModsObjectOutcome Pulled(OtherObject placed) => new(placed, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 40f, ReferenceRoles.None);
        var result = ObjectsOnly(Restored(rock, Cleared)) with
        {
            FollowUp = new RestoreFollowUp(
                [],
                [new FollowUpOtherObject(Pulled(alone), []) { ByGround = true }, new FollowUpOtherObject(Pulled(withRock), [rock.Key]) { ByGround = true }]),
        };

        var detailed = LogSections.BuildRestoreWhereRemovedSection(world, result, Elapsed, Detailed with { Bases = TestBaseFacts.Unresolved }).Lines;
        var table = Assert.Single(Tables.BuildRestoredWhereRemovedTables(world, result, [], Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedFollowUpFileName);

        Assert.Contains(detailed, line => line.StartsWith($"  Follow-up of what went back: Lantern [{alone.Key}]", StringComparison.Ordinal) && line.EndsWith("pulled down by 40.0 units, with the ground going back down", StringComparison.Ordinal));
        Assert.Contains(
            detailed,
            line => line.StartsWith($"  Follow-up of what went back: Lamp [{withRock.Key}]", StringComparison.Ordinal)
                && line.EndsWith($"pulled down by 40.0 units, with {RecordLabels.Of(rock.Key, null)} going back and the ground going back down", StringComparison.Ordinal));
        Assert.Equal(["ground", $"{rock.Key} ground"], table.Rows.Select(row => row[9].Trim('"')));
    }

    /// <summary>
    /// Two rocks go back and would float where they arrive: the follow-up then pulls one down and removes the other. Each keeps its
    /// restored row, which names what the follow-up then did, and the counts of what went back state both.
    /// </summary>
    [Fact]
    public void ARestoredObjectTheFollowUpThenTakesAwayNamesItsFollowUp()
    {
        var world = World();
        var (pulled, removed) = (Rock(0x913, PlacedParts.Position), Rock(0x914, PlacedParts.Position));
        var result = ObjectsOnly(Restored(pulled, Cleared), Restored(removed, Cleared)) with
        {
            FollowUp = new RestoreFollowUp([], [ArrivalFollowedUp(pulled, OtherModsObjectFate.PulledDown), ArrivalFollowedUp(removed, OtherModsObjectFate.Removed)]),
        };

        var normal = LogSections.BuildRestoreWhereRemovedSection(world, result, Elapsed, Normal).Lines;
        var summary = Assert.Single(LogSections.BuildRestoreWhereRemovedSummarySection(result).Lines);
        var detailed = LogSections.BuildRestoreWhereRemovedSection(world, result, Elapsed, Detailed with { Bases = TestBaseFacts.Unresolved }).Lines;
        var table = Assert.Single(Tables.BuildRestoredWhereRemovedTables(world, result, [], Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedObjectsFileName);

        const string Followed = "1 of them then pulled down by the follow-up of what went back, 1 of them then removed by the follow-up of what went back.";
        Assert.Contains($"{Heading}: 2 objects of other mods (2 moved back), {Followed}", normal);
        Assert.Equal($"{Heading}: 2 objects, {Followed}", summary);
        const string PulledText = "then pulled down to (10, 20, 0) by the follow-up of what went back";
        const string RemovedText = "then removed by the follow-up of what went back";
        Assert.Contains(detailed, line => line.StartsWith($"  Moved back: {RecordLabels.Of(pulled.Key, null)}", StringComparison.Ordinal) && line.Contains($" to (10, 20, 30); {PulledText};", StringComparison.Ordinal));
        Assert.Contains(detailed, line => line.StartsWith($"  Moved back: {RecordLabels.Of(removed.Key, null)}", StringComparison.Ordinal) && line.Contains($" to (10, 20, 30); {RemovedText};", StringComparison.Ordinal));
        Assert.Equal([$"restored; {PulledText}", $"restored; {RemovedText}"], table.Rows.Select(row => Field(table, row, "result")));
    }

    /// <summary>The follow-up of a placed record where it arrives at (10, 20, 30), started by itself; pulled down onto the ground at height 0.</summary>
    private static FollowUpOtherObject ArrivalFollowedUp(RecordChangedByCleanedMod change, OtherModsObjectFate fate)
    {
        var arrived = TestShapes.Placed(Mutagen.Bethesda.Plugins.ModKey.FromNameAndExtension("Base.esm"), 0, new BaseKey(Key(0x300), BaseLinkKind.PlaceableObject), new Vector3(10, 20, 30)) with { Key = change.Key };
        var outcome = new OtherModsObjectOutcome(arrived, fate, LostShare: 1f, Drop: fate == OtherModsObjectFate.PulledDown ? 30f : 0f, ReferenceRoles.None);
        return new FollowUpOtherObject(outcome, [change.Key]);
    }
}
