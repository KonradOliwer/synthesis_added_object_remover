namespace AddedObjectRemover.Tests.Rules;

public class FollowUpParticipantTests
{
    [Fact]
    public void InvisibleTargetsWithAMeshNeverTouchOrSupport()
    {
        var looks = new TargetLooks(
        [
            ObjectVisibility.Visible,
            ObjectVisibility.Invisible(InvisibleObjectKind.IdleMarkers),
            ObjectVisibility.Invisible(InvisibleObjectKind.MapMarkers),
            ObjectVisibility.Visible,
            ObjectVisibility.Visible,
        ]);
        var meshPaths = new TargetMeshPaths(["a.nif", "idle.nif", "map.nif", null, "b.nif"]);

        var excluded = TouchSearch.MarkExcluded(looks, excluded: [4], meshPaths);

        Assert.Equal(new[] { false, true, true, true, true }, excluded);
    }
}
