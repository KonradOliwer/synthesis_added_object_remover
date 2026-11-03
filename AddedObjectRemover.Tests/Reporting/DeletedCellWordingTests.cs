using AddedObjectRemover.Steps.BuildLogAndReports;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>A left-behind object kept because its cell is deleted names the cell and the deleting plugin, and is not called referenced.</summary>
public class DeletedCellWordingTests
{
    private const string Expected = "in a deleted cell: its cell DeletedCell [000D00:Skyrim.esm] is deleted by CityMod.esp";

    [Fact]
    public void AnInvisibleObjectKeptInADeletedCellNamesTheCellAndThePlugin()
    {
        var evaluation = new InvisibleLeftBehindCheck(
            0, InvisibleObjectKind.XMarkers, 100f, null, new SectorAreaTally(50).Build(), LeftBehindOutcome.KeptInDeletedCell, TestKeepReasons.InDeletedCell);

        Assert.Equal(Expected, LeftBehindOutcomeText.DescribeReason(evaluation));
        Assert.Equal("kept", LeftBehindOutcomeText.Verdict(evaluation));
    }

    [Fact]
    public void AVisibleObjectKeptInADeletedCellNamesTheCellAndThePlugin()
    {
        var evaluation = new VisibleLeftBehindCheck(
            0, null, new SectorAreaTally(50).Build(), LeftBehindOutcome.KeptInDeletedCell, TestKeepReasons.InDeletedCell);

        Assert.Equal(Expected, LeftBehindOutcomeText.DescribeReason(evaluation));
        Assert.Equal("kept", LeftBehindOutcomeText.Verdict(evaluation));
    }
}
