namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>A run with the default settings for other mods' objects the cleaned mod changed, on the fixture scene, which holds none.</summary>
public class DefaultChangedObjectsRunTests
{
    private const int Workers = 4;

    [Fact]
    public void ARunWithTheDefaultSettingsAndNoChangedObjectDecidesNoOtherModsObjectASecondTime()
    {
        var settings = SettingsVariants.Of(SettingsVariants.Support);
        Assert.Equal(RestoreMode.RestoreIfNoCollision, settings.Standard.ChangedObjects.Moved.WhenRemoved);

        var outcome = FixtureRun.Execute(settings, Workers);

        Assert.All(outcome.Collected.Targets, target => Assert.Equal(TargetOrigin.CleanedMod, target.Origin));
        Assert.NotEmpty(outcome.Collected.OtherModAndSupportOnlyObjects);
        Assert.All(outcome.Collected.OtherModAndSupportOnlyObjects, other => Assert.Null(other.ChangeWithoutCleanedMod));
        Assert.Empty(outcome.Restorations);
    }
}
