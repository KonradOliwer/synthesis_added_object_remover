namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// A whole run on a site of the cleaned mod whose solid pieces a chain of removals takes away, from one crate too close to a city pad
/// through every crate touching it, while its mists, drawn only with an effect shader, are left standing among them.
/// </summary>
public sealed class EffectOnlyPiecesOfARemovedSiteRunTests
{
    [Fact]
    public void TheMistsOfASiteWhoseSolidPiecesWereRemovedAreRemovedTooAndTheBaseGamePiecesComeBack()
    {
        var (outcome, _) = RestoreWhereRemovedRunTests.Run(SettingsWithTheSiteSteps(), RestoreWhereRemovedWorlds.CreateSiteWithMists);

        var targets = outcome.Collected.Targets;
        var removedMists = outcome.Removals.OfType<ObjectWithReason.VisibleLeftBehind>().Select(removal => targets[removal.TargetIndex].EditorId);
        Assert.Equal(["T_Mist11", "T_Mist13"], removedMists.Order(StringComparer.Ordinal));
        Assert.Contains(outcome.Removals, removal => removal is ObjectWithReason.TooClose && targets[removal.TargetIndex].EditorId == "T_Block00");
        Assert.All(targets.Where(target => target.Origin == TargetOrigin.CleanedMod), target => Assert.Contains(outcome.Removals, removal => removal.TargetIndex == target.Id.Index));
        var judged = outcome.RestoreWhereRemoved.Objects.ToDictionary(change => change.Change.EditorId!);
        Assert.Equal((true, AreaVerdict.Cleared), (judged["B_Hidden"].IsRestored, judged["B_Hidden"].OldPlace!.Verdict));
        Assert.True(judged["B_Moved"].IsRestored);
    }

    /// <summary>The settings of the restore runs, with the left-behind step for visible objects on and every touching object removed with a removed one.</summary>
    private static Settings SettingsWithTheSiteSteps()
    {
        var settings = RestoreWhereRemovedRunTests.SettingsOf();
        settings.Standard.LeftoverVisibleObjects.RemoveLeftoverVisibleObjects = true;
        settings.Standard.FollowUpRemoval.Mode = FollowUpRemovalMode.EverythingTouchingIt;
        return settings;
    }
}
