using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using static AddedObjectRemover.Tests.EndToEnd.MovedReplacementScenes;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The report files and detailed log lines of whole runs of base-game objects that the cleaned mod moved or replaced.</summary>
public class MovedReplacementReportTests
{
    [Fact]
    public void TheChangedAndFittedObjectsFileHasAKindColumnAndNamesTheEarlierBaseOfARestoredReplacedObject()
    {
        var settings = CrateSettings().WithReplacedObjects(process: true, SettingDefaults.ReplacedObjectsWhenRemoved);
        settings.DetailedLogAndReports = true;

        var (_, run) = FixtureRun.Capture(settings, Workers, CreateReplacedWorld);

        var fits = Assert.Single(run.Reports, report => report.Name == "changed-and-fitted-objects.csv");
        Assert.Equal("kind,outcome,formKey,editorId,base,worldspace,x,y,z,rotZDeg,scale,newX,newY,newZ,newRotZDeg,newScale,newBase", fits.Lines[0]);
        Assert.StartsWith("replaced,changed object restored,", fits.Lines[1]);
        Assert.EndsWith(",B_CrateBase [000804:Skyrim.esm]", fits.Lines[1]);
        Assert.Contains(run.Log, line => line.StartsWith("Changed and fitted objects: wrote 1 rows to ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheChangedAndFittedObjectsFileLeavesTheNewBaseEmptyForAMovedObjectRestoredWithItsOwnBase()
    {
        var (_, run) = RunRestoring(RestoreMode.Restore, dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        var row = Assert.Single(run.Reports, report => report.Name == "changed-and-fitted-objects.csv").Lines[1];
        Assert.StartsWith("moved,changed object restored,", row);
        Assert.EndsWith(",1,0,0,0,0,1,", row);
    }

    /// <summary>The crate stays where the cleaned mod put it; the step judged it and keeps it, and says so in the log and in its file.</summary>
    [Fact]
    public void AMovedReplacementKeptWhereTheCleanedModPutItIsJudgedForRestoringWhereItWasRemovedAndSaidSoInTheLogAndTheFile()
    {
        var settings = CrateSettings();
        settings.DetailedLogAndReports = true;

        var (_, run) = FixtureRun.Capture(settings, Workers, dataFolder => CreateCrateWorld(dataFolder, MovedTo, []));

        Assert.Contains(run.Log, line => line.StartsWith("  Kept as changed: B_Crate [000805:Skyrim.esm]", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Log, line => line.StartsWith("Restored where the cleaned mod was removed", StringComparison.Ordinal));
        Assert.Matches(@"^Restore where removed: \d+ pass(es)? in ", run.Log.Single(line => line.StartsWith("Restore where removed: ", StringComparison.Ordinal)));
        var objects = Assert.Single(run.Reports, report => report.Name == ReportFileNames.RestoredWhereRemovedObjectsFileName);
        Assert.StartsWith("formKey,editorId,base,space,cellFormKey,cellEditorId,winningMod,oldX,oldY,oldZ,undone,leftToLaterPlugins,objects,", objects.Lines[0]);
        Assert.StartsWith("000805:Skyrim.esm,B_Crate,", Assert.Single(objects.Lines.Skip(1)));
        Assert.Contains(",kept: ", objects.Lines[1], StringComparison.Ordinal);
        Assert.Contains(run.Log, line => line.StartsWith("Objects judged for restoring where the cleaned mod was removed: wrote 1 rows to ", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Reports, report => report.Name == ReportFileNames.RestoredWhereRemovedCellsFileName);
    }

    [Fact]
    public void WithTheRestoreStepSwitchedOffItPrintsAndWritesNothing()
    {
        var settings = CrateSettings();
        settings.DetailedLogAndReports = true;
        settings.Standard.RestoreWhereRemoved = new RestoreWhereRemovedSettings
        {
            RestoreOtherModsObjects = false, RestoreTerrain = false, RestoreCells = false, RestoreNavmeshes = false,
        };

        var (_, run) = FixtureRun.Capture(settings, Workers, dataFolder => CreateCrateWorld(dataFolder, MovedTo, []));

        Assert.DoesNotContain(run.Log, line => line.Contains("Restore where removed", StringComparison.Ordinal) || line.Contains("restoring where", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Reports, report => report.Name.StartsWith("restored-where-removed", StringComparison.Ordinal));
    }

    [Fact]
    public void TheChangedAndFittedObjectsFileHasAnOwnRowForAnObjectOfTheCleanedModKeptShrunk()
    {
        var settings = SupportSettings();
        settings.DetailedLogAndReports = true;

        var (_, run) = FixtureRun.Capture(settings, Workers, dataFolder => CreateStackWorld(dataFolder, [BesideTheTop]));

        var fits = Assert.Single(run.Reports, report => report.Name == "changed-and-fitted-objects.csv");
        var row = Assert.Single(fits.Lines.Skip(1));
        Assert.StartsWith("own,own object shrunk or turned,", row);
        Assert.Equal(17, row.Split(',').Length);
        Assert.EndsWith(",", row);
    }
}
