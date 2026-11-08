using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

using static RestoreWhereRemovedReportScene;

/// <summary>The two report tables of the step that restores what the cleaned mod changed where it was removed, over results built by hand.</summary>
public sealed class RestoreWhereRemovedReportTests
{
    [Fact]
    public void TheObjectsFileHasARowPerJudgedPlacedChangeWithTheAreaAndTheResult()
    {
        var table = ObjectsTable();

        Assert.Equal(Scene().Objects.Length, table.Rows.Length);
        Assert.All(table.Rows, row => Assert.Equal(table.Header.Length, row.Length));
        var moved = Row(table, "formKey", Key(0x901).ToString());
        Assert.Equal(
            ("10", "20", "30", "position, rotation", "rotation (Later.esp)", "214", "0", "8000000", "0", "0", "0", "Cleared", "restored"),
            (Field(table, moved, "oldX"), Field(table, moved, "oldY"), Field(table, moved, "oldZ"), Field(table, moved, "undone"), Field(table, moved, "leftToLaterPlugins"),
                Field(table, moved, "objects"), Field(table, moved, "standing"), Field(table, moved, "size"), Field(table, moved, "standingSize"),
                Field(table, moved, "standingPercentByCount"), Field(table, moved, "standingPercentBySize"), Field(table, moved, "verdict"), Field(table, moved, "result")));
        var standing = Row(table, "formKey", Key(0x907).ToString());
        Assert.Equal(
            ("10", "4", "40", "25", "StillStanding", "kept: the cleaned mod's objects still stand around it"),
            (Field(table, standing, "objects"), Field(table, standing, "standing"), Field(table, standing, "standingPercentByCount"), Field(table, standing, "standingPercentBySize"),
                Field(table, standing, "verdict"), Field(table, standing, "result")));
    }

    [Fact]
    public void TheObjectsFileLeavesTheAreaOfARecordThatNeverGoesBackEmpty()
    {
        var never = Row(ObjectsTable(), "formKey", Key(0x909).ToString());
        var objects = ObjectsTable();

        Assert.Equal(
            ("", "", "", "kept: a plugin that loads after the patch has the last word on it (Later.esp)"),
            (Field(objects, never, "objects"), Field(objects, never, "standing"), Field(objects, never, "verdict"), Field(objects, never, "result")));
    }

    [Fact]
    public void TheCellsFileHasARowPerCellTerrainQuarterThatHoldsAChangeAndNavmeshInThatOrder()
    {
        var table = CellsTable();

        Assert.All(table.Rows, row => Assert.Equal(table.Header.Length, row.Length));
        Assert.Equal(
            ["cell", "cell", "cell", "cell", "quarter", "quarter", "navmesh", "navmesh", "navmesh", "navmesh", "navmesh"],
            table.Rows.Select(row => Field(table, row, "kind")));
    }

    [Fact]
    public void ACellRowNamesItsGridPartsAndWhatALaterPluginChangedAgain()
    {
        var table = CellsTable();

        var row = table.Rows[2];

        Assert.Equal(
            ("5", "6", "music", "music (Later.esp)", "StillStanding", "kept: the cleaned mod's objects still stand around it"),
            (Field(table, row, "gridX"), Field(table, row, "gridY"), Field(table, row, "undone"), Field(table, row, "leftToLaterPlugins"), Field(table, row, "verdict"), Field(table, row, "result")));
    }

    [Fact]
    public void AQuarterRowCountsItsPointsAndTheHeightsKeptPerReason()
    {
        var table = CellsTable();

        var south = table.Rows.Single(row => Field(table, row, "quarter") == "south-west");

        Assert.Equal(
            ("0", "0", "3 heights, 1 vertex colour, ground textures", "restored", "1", "0"),
            (Field(table, south, "gridX"), Field(table, south, "gridY"), Field(table, south, "undone"), Field(table, south, "result"), Field(table, south, "keptUnderObjectThatStays"), Field(table, south, "keptTooSteepToStore")));
        var north = table.Rows.Single(row => Field(table, row, "quarter") == "north-east");
        Assert.Equal("kept: the cleaned mod's objects still stand around it", Field(table, north, "result"));
    }

    /// <summary>The cleaned mod changed only the rest of a land record, whose quarters hold no height, colour or texture to put back.</summary>
    [Theory]
    [InlineData(false, "restored")]
    [InlineData(true, "kept: the cleaned mod's objects still stand around it")]
    public void ALandWhoseOnlyChangeIsTheRestOfTheRecordGetsARowOfItsOwn(bool aQuarterStillStands, string result)
    {
        var scene = Scene() with { Objects = [], Cells = [], Navmeshes = [], NavmeshGroups = [], Lands = [RestOnlyLand(aQuarterStillStands)] };

        var table = Assert.Single(BuildTables(scene, Normal));

        var row = Assert.Single(table.Rows);
        Assert.Equal(
            ("rest", "1", "0", "", "the rest of the record", result),
            (Field(table, row, "kind"), Field(table, row, "gridX"), Field(table, row, "gridY"), Field(table, row, "quarter"), Field(table, row, "undone"), Field(table, row, "result")));
    }

    /// <summary>The same land as above: kept, it has a detailed line and a count of its own; restored, the normal log names no quarter.</summary>
    [Fact]
    public void ALandWhoseOnlyChangeIsTheRestOfTheRecordIsNamedWhenKeptAndCountedWithoutQuartersWhenRestored()
    {
        var kept = Scene() with { Objects = [], Cells = [], Navmeshes = [], NavmeshGroups = [], Lands = [RestOnlyLand(lastQuarterStillStands: true)] };
        var restored = kept with { Lands = [RestOnlyLand(lastQuarterStillStands: false)] };

        var keptLines = Section(kept, Detailed).Lines;

        Assert.Contains($"  Kept as changed: terrain {Space}, cell (1, 0): the rest of the record: the cleaned mod's objects still stand around it", keptLines);
        Assert.Contains("Kept because the cleaned mod's objects still stand around them: the rest of 1 land record.", keptLines);
        Assert.Equal($"{Heading}: terrain of 1 cell.", Section(restored, Normal).Lines[0]);
    }

    /// <summary>
    /// Cell (2, 0): the cleaned mod raised a point inside the south-west quarter and one on its line to the south-east quarter, whose
    /// area still stands. The south-west quarter's heights add up: one restored, one kept for the quarter that stays; the south-east
    /// quarter keeps its point for its own area.
    /// </summary>
    [Fact]
    public void AQuartersHeightsAreRestoredOrKeptForAReasonAndAPointOnTheLineToAQuarterThatStaysSaysSo()
    {
        var scene = Scene() with { Objects = [], Cells = [], Navmeshes = [], NavmeshGroups = [], Lands = [LandNextToAQuarterThatStays()] };

        var lines = Section(scene, Detailed).Lines;
        var table = Assert.Single(BuildTables(scene, Normal));

        Assert.Contains(
            $"  Terrain {Space}, cell (2, 0), south-west quarter: 1 height; 1 height kept on the line to a quarter of the cell whose area is not cleared; 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
        Assert.Contains("Left as the cleaned mod changed them: terrain at 1 point (1 on the line to a quarter of the cell whose area is not cleared).", lines);
        var south = Row(table, "quarter", "south-west");
        var east = Row(table, "quarter", "south-east");
        Assert.Equal(("2 heights", "1"), (Field(table, south, "undone"), Field(table, south, "keptNextToQuarterThatStays")));
        Assert.Equal(("1 height", "0", "kept: the cleaned mod's objects still stand around it"), (Field(table, east, "undone"), Field(table, east, "keptNextToQuarterThatStays"), Field(table, east, "result")));
    }

    [Fact]
    public void AClearedQuarterKeepingOnlyVertexColoursSaysTheyLieOnALineSharedWithGroundThatStays()
    {
        var land = LandNextToAQuarterThatStays();
        var coloursOnly = LandOutcomes.Judged(
            land.Change with { UndoPoints = PointSet.Of([]), UndoColours = PointSet.Of([16]) },
            land.Quarters,
            heights: PointSet.Of([]),
            land.Colours,
            land.Textures,
            texturesKept: default,
            rest: false,
            kept: [],
            finalHeights: null,
            CellEdgeHeights.Unknown);
        var scene = Scene() with { Objects = [], Cells = [], Navmeshes = [], NavmeshGroups = [], Lands = [coloursOnly] };

        var table = Assert.Single(BuildTables(scene, Normal));

        Assert.Equal("kept: its vertex colours lie on a line shared with ground that stays", Field(table, Row(table, "quarter", "south-west"), "result"));
    }

    /// <summary>A size of a real area passes what a float holds exactly.</summary>
    [Fact]
    public void TheSizeColumnsKeepEveryDigitOfALargeArea()
    {
        var large = Cleared with { Size = 123_456_789d, StandingSize = 12_345_678d };

        var table = Assert.Single(BuildTables(ObjectsOnly(Restored(Rock(0x914, PlacedParts.Position), large)), Normal));

        Assert.Equal(("123456789", "12345678"), (Field(table, table.Rows[0], "size"), Field(table, table.Rows[0], "standingSize")));
    }

    [Fact]
    public void ANavmeshRowNamesItsCellItsPartsAndTheReasonItsGroupStays()
    {
        var table = CellsTable();

        var refused = Row(table, "navmesh", Key(0xA04).ToString());
        var restored = Row(table, "navmesh", Key(0xA01).ToString());

        Assert.Equal(
            (Key(0x600).ToString(), $"kept: linked to navmeshes that stay ({Key(0xA09)})"),
            (Field(table, refused, "cell"), Field(table, refused, "result")));
        Assert.Equal(("shape", "restored"), (Field(table, restored, "undone"), Field(table, restored, "result")));
    }

    [Fact]
    public void BothFilesAreWrittenOnlyWithARowAndNeverOutOfTheListOfKnownNames()
    {
        var onlyCells = Scene() with { Objects = [], Lands = [], Navmeshes = [], NavmeshGroups = [] };

        var tables = BuildTables(onlyCells, Normal);

        Assert.Equal([ReportFileNames.RestoredWhereRemovedCellsFileName], tables.Select(table => table.FileName));
    }

    [Fact]
    public void TheObjectsFileNamesTheCopyWrittenAndWhyARecordMayNotBeCopied()
    {
        var copy = new RecordKey(Patch, 0x800);

        var table = Assert.Single(BuildTables(CopyScene(), Normal, new CloneWritten(Key(0x930), copy)));

        var copied = Row(table, "formKey", Key(0x930).ToString());
        Assert.Equal(("restored with a copy at the cleaned mod's place", copy.ToString(), ""), (Field(table, copied, "result"), Field(table, copied, "copyFormKey"), Field(table, copied, "copyRefusedBy")));
        var held = Row(table, "formKey", Key(0x931).ToString());
        Assert.Equal(("", "persistent"), (Field(table, held, "copyFormKey"), Field(table, held, "copyRefusedBy")));
        Assert.Equal("not plain scenery", Field(table, Row(table, "formKey", Key(0x932).ToString()), "copyRefusedBy"));
        Assert.All(table.Rows, row => Assert.Equal(table.Header.Length, row.Length));
    }

    [Fact]
    public void TheCellsFileNamesTheTexturesAndTheWaterLevelThatStayInAQuarterOrCellThatGoesBack()
    {
        var land = Land();
        var texturesKept = LandOutcomes.Judged(
            land.Change, land.Quarters, land.Heights, land.Colours, textures: QuarterSet.Of([]), texturesKept: QuarterSet.Of([0]), rest: false, land.Kept, finalHeights: null, CellEdgeHeights.Unknown);
        var scene = Scene() with { Objects = [], Navmeshes = [], NavmeshGroups = [], Lands = [texturesKept] };

        var table = Assert.Single(BuildTables(scene, Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedCellsFileName);

        Assert.Equal($"restored; ground textures kept: {RestoreWhereRemovedText.TexturesKeptForObjectThatStays}", Field(table, Row(table, "quarter", "south-west"), "result"));
        Assert.Equal(
            $"restored; water level kept: {WaterObject} lies across the water line between the level before the cleaned mod and the one that stays",
            Field(table, Row(table, "gridX", "3"), "result"));
    }
}
