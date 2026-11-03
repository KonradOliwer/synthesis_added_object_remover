namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// A whole run on the village world, where the other mods' removals take both crates of the cleaned mod and a base-game rock the
/// cleaned mod disabled goes back, while a lone small object of the cleaned mod still stands beside the statue's place: the area is
/// cleared, so that leftover is removed too, and the restore step's leftovers file and log say so.
/// </summary>
public sealed class LeftoversInRestoredAreasRunTests
{
    private const string LeftoverRemoved = "removed as a leftover where the area was restored";

    [Fact]
    public void ALoneObjectOfTheCleanedModStandingWhereTheAreaIsRestoredIsRemovedAndNamedInTheLeftoversFileAndTheLog()
    {
        var (_, run) = RestoreWhereRemovedRunTests.Run(RestoreWhereRemovedRunTests.SettingsOf(), RestoreWhereRemovedWorlds.CreateVillage);

        var leftover = Assert.Single(Rows(run, ReportFileNames.RestoredWhereRemovedLeftoversFileName));
        Assert.Equal(("T_Pebble", "interior AorInterior [000800:Skyrim.esm]", "", "", LeftoverRemoved), (leftover["editorId"], leftover["space"], leftover["areaCentreX"], leftover["areaCentreY"], leftover["result"]));
        var rock = Assert.Single(Rows(run, ReportFileNames.RestoredWhereRemovedObjectsFileName), row => row["editorId"] == "B_Rock");
        Assert.Equal("restored", rock["result"]);
        Assert.Contains($"Objects of the cleaned mod still standing in restored areas: 1 {LeftoverRemoved}.", run.Log);
        Assert.Contains(run.Patch, line => line.StartsWith("000804:Target.esp in 000800:Skyrim.esm", StringComparison.Ordinal) && line.Contains("disabled True", StringComparison.Ordinal));
    }

    /// <summary>In an exterior the leftovers file names the column of the restored area the object stands in.</summary>
    [Fact]
    public void ALoneObjectStandingInARestoredAreaOfTheExteriorIsNamedWithTheCentreOfItsColumn()
    {
        var (_, run) = RestoreWhereRemovedRunTests.Run(
            RestoreWhereRemovedRunTests.SettingsOf(restore => restore.ClearedArea.SearchRadius = RestoreWhereRemovedRunTests.SmallSearchRadius),
            RestoreWhereRemovedWorlds.CreateTerrainWithALoneObject);

        var leftover = Assert.Single(Rows(run, ReportFileNames.RestoredWhereRemovedLeftoversFileName));
        Assert.Equal(("T_Pebble", LeftoverRemoved), (leftover["editorId"], leftover["result"]));
        Assert.NotEqual("", leftover["areaCentreX"]);
        Assert.NotEqual("", leftover["areaCentreY"]);
        ExpectedOutputFiles.AssertMatches("restore-where-removed/TerrainWithALoneObject", run.ToExpectedOutputLines());
    }

    private static List<Dictionary<string, string>> Rows(RunOutput run, string fileName) => ReportRows.Of(Assert.Single(run.Reports, report => report.Name == fileName));
}
