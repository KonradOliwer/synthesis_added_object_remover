using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// A whole run on an interior where the cleaned mod's crates are removed as too close, all but one: the area method of "Objects left
/// behind" removes that last crate, the invisible step sees it as removed, and the restore judges the interior on the decisions with
/// it, so a change the cleaned mod made there goes back only because of the area method.
/// </summary>
public sealed class ObjectsInClearedAreasRunTests
{
    [Fact]
    public void TheLastStandingCrateIsRemovedInAClearedAreaAndTheRestoreThenPutsBackTheDisabledRock()
    {
        var (outcome, _) = RestoreWhereRemovedRunTests.Run(SettingsOf(areaMethod: true), RestoreWhereRemovedWorlds.CreateRowWithALoneCrate);

        var lone = Assert.Single(outcome.Removals, removal => EditorIdOf(outcome, removal.TargetIndex) == "T_LoneCrate");
        Assert.Equal((1, 21), (Assert.IsType<ObjectWithReason.InClearedArea>(lone).Area.Standing, ((ObjectWithReason.InClearedArea)lone).Area.Objects));
        Assert.True(Rock(outcome).IsRestored);
    }

    [Fact]
    public void WithTheAreaMethodOffTheCrateStaysAndTheRockStaysDisabled()
    {
        var (outcome, _) = RestoreWhereRemovedRunTests.Run(SettingsOf(areaMethod: false), RestoreWhereRemovedWorlds.CreateRowWithALoneCrate);

        Assert.DoesNotContain(outcome.Removals, removal => EditorIdOf(outcome, removal.TargetIndex) == "T_LoneCrate");
        Assert.Equal(StepOutcome.SwitchedOff, outcome.ObjectsInClearedAreas.Outcome);
        Assert.False(Rock(outcome).IsRestored);
    }

    [Fact]
    public void TheInvisibleStepSeesTheCrateTheAreaMethodRemovedAsRemovedSurroundings()
    {
        var on = Marker(RestoreWhereRemovedRunTests.Run(SettingsOf(areaMethod: true), RestoreWhereRemovedWorlds.CreateRowWithALoneCrate).Outcome);
        var off = Marker(RestoreWhereRemovedRunTests.Run(SettingsOf(areaMethod: false), RestoreWhereRemovedWorlds.CreateRowWithALoneCrate).Outcome);

        Assert.True(on.Surroundings.Total(DirectionSector.West) > 0);
        Assert.Equal(on.Surroundings.Total(DirectionSector.West), on.Surroundings.Removed(DirectionSector.West));
        Assert.Equal(0f, off.Surroundings.Removed(DirectionSector.West));
    }

    [Fact]
    public void TheAreaMethodKeepsAnInvisibleKindTheInvisibleStepAlwaysKeeps()
    {
        var (outcome, _) = RestoreWhereRemovedRunTests.Run(SettingsOf(areaMethod: true), RestoreWhereRemovedWorlds.CreateRowWithALoneCrate);

        var marker = Assert.Single(outcome.ObjectsInClearedAreas.Evaluations, evaluation => EditorIdOf(outcome, evaluation.TargetIndex) == "T_Marker");
        Assert.Equal((ClearedAreaObjectKind.Invisible, LeftBehindOutcome.KeptProtectedType), (marker.Kind, marker.Decision));
    }

    [Fact]
    public void ABaseGameObjectOnTheRemovedCrateIsPulledDownToTheFloorWhenItIsPulledDown()
    {
        var (outcome, run, pad) = RunCarryingAPad(OtherModsObjectsHandling.PullObjectToTheGround, areaMethod: true);

        Assert.IsType<ObjectWithReason.InClearedArea>(Assert.Single(outcome.Removals, removal => EditorIdOf(outcome, removal.TargetIndex) == "T_LoneCrate"));
        Assert.Contains($"disabled False, position ({LoneCrateX:F2}, {LoneCrateY:F2}, 0.00)", PatchLineOf(run, pad));
    }

    [Fact]
    public void ABaseGameObjectOnTheRemovedCrateIsRemovedWhenFloatingObjectsAreRemoved()
    {
        var (outcome, run, pad) = RunCarryingAPad(OtherModsObjectsHandling.RemoveFloatingObject, areaMethod: true);

        Assert.IsType<ObjectWithReason.InClearedArea>(Assert.Single(outcome.Removals, removal => EditorIdOf(outcome, removal.TargetIndex) == "T_LoneCrate"));
        Assert.Contains("disabled True", PatchLineOf(run, pad));
    }

    [Fact]
    public void TheCrateCarryingABaseGameObjectStaysWhenSupportingObjectsAreKept()
    {
        var (outcome, run, pad) = RunCarryingAPad(OtherModsObjectsHandling.KeepSupportingObject, areaMethod: true);

        Assert.DoesNotContain(outcome.Removals, removal => EditorIdOf(outcome, removal.TargetIndex) == "T_LoneCrate");
        Assert.DoesNotContain(run.Patch, line => line.StartsWith(pad.FormKey + " in ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(OtherModsObjectsHandling.PullObjectToTheGround)]
    [InlineData(OtherModsObjectsHandling.RemoveFloatingObject)]
    [InlineData(OtherModsObjectsHandling.KeepSupportingObject)]
    public void WithTheAreaMethodOffTheBaseGameObjectIsNotTouched(OtherModsObjectsHandling handling)
    {
        var (outcome, run, pad) = RunCarryingAPad(handling, areaMethod: false);

        Assert.DoesNotContain(outcome.Removals, removal => EditorIdOf(outcome, removal.TargetIndex) == "T_LoneCrate");
        Assert.DoesNotContain(run.Patch, line => line.StartsWith(pad.FormKey + " in ", StringComparison.Ordinal));
    }

    private static (RunOutcome Outcome, RunOutput Run, PlacedObject Pad) RunCarryingAPad(OtherModsObjectsHandling handling, bool areaMethod)
    {
        var settings = SettingsOf(areaMethod);
        settings.Standard.FollowUpRemoval.OtherModsObjectsLeftWithoutSupport = handling;
        PlacedObject? pad = null;
        var (outcome, run) = RestoreWhereRemovedRunTests.Run(settings, dataFolder =>
        {
            var (loadOrder, placed) = RestoreWhereRemovedWorlds.CreateRowWithALoneCrateCarryingAPad(dataFolder);
            pad = placed;
            return loadOrder;
        });
        return (outcome, run, pad!);
    }

    private static string PatchLineOf(RunOutput run, PlacedObject record) =>
        Assert.Single(run.Patch, line => line.StartsWith(record.FormKey + " in ", StringComparison.Ordinal));

    private static float LoneCrateX => RestoreWhereRemovedWorlds.LoneCrateAt.X;

    private static float LoneCrateY => RestoreWhereRemovedWorlds.LoneCrateAt.Y;

    /// <summary>
    /// The area method with 5% for both shares, so 1 crate of 21 standing clears the interior; the restore with 1% for both, so it
    /// does not while that crate stands. The marker's kind is always kept by the invisible step, so both steps keep and measure it.
    /// </summary>
    private static Settings SettingsOf(bool areaMethod)
    {
        var settings = RestoreWhereRemovedRunTests.SettingsOf(restore => restore.ClearedArea = new ClearedAreaSettings { MaxStandingSizePercent = 1, MaxStandingCountPercent = 1 });
        settings.Standard.LeftoverObjects.RemoveObjectsInClearedAreas = areaMethod;
        settings.Standard.LeftoverObjects.ClearedArea = new LeftoverClearedAreaSettings { MaxStandingSizePercent = 5, MaxStandingCountPercent = 5 };
        settings.Standard.LeftoverInvisibleObjects.AlwaysKeep.XMarkers = true;
        return settings;
    }

    private static string? EditorIdOf(RunOutcome outcome, int targetIndex) => outcome.Collected.Targets[targetIndex].EditorId;

    private static PlacedChangeJudged Rock(RunOutcome outcome) => Assert.Single(outcome.RestoreWhereRemoved.Objects, change => change.Change.EditorId == "B_Rock");

    private static InvisibleLeftBehindCheck Marker(RunOutcome outcome) =>
        Assert.Single(outcome.InvisibleLeftBehind.Evaluations, evaluation => EditorIdOf(outcome, evaluation.TargetIndex) == "T_Marker");
}
