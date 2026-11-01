namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>A run with the default settings for other mods' objects the cleaned mod changed, on the fixture scene, which holds none.</summary>
public class DefaultChangedObjectsRunTests
{
    private const int Workers = 4;

    /// <summary>
    /// Without a changed object only restoring other mods' objects where the cleaned mod was removed asks for the world without the
    /// cleaned mod, and only when a placed record of another plugin the cleaned mod changed can go back; the scene has none, so no
    /// object is decided a second time whatever that switch says.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARunWithTheDefaultSettingsAndNoChangedObjectOrChangedRecordDecidesNoOtherModsObjectASecondTime(bool restoreOtherModsObjects)
    {
        var settings = SettingsVariants.Of(SettingsVariants.Support);
        settings.Standard.RestoreWhereRemoved.RestoreOtherModsObjects = restoreOtherModsObjects;
        Assert.Equal(RestoreMode.RestoreIfNoCollision, settings.Standard.ChangedObjects.Moved.WhenRemoved);

        var outcome = FixtureRun.Execute(settings, Workers);

        Assert.All(outcome.Collected.Targets, target => Assert.Equal(TargetOrigin.CleanedMod, target.Origin));
        Assert.Empty(outcome.Collected.ChangedRecords);
        Assert.NotEmpty(outcome.Collected.OtherModAndSupportOnlyObjects);
        Assert.DoesNotContain(outcome.Collected.OtherModAndSupportOnlyObjects, other => other.ChangeWithoutCleanedMod is not null);
        Assert.Empty(outcome.Restorations);
    }
}
