using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

public class RestingObjectsParticipantTests
{
    [Fact]
    public void InvisibleTargetsWithAMeshNeverTouchOrSupport()
    {
        var shapes = TestVisibility.Over(TestShapes.Create(TestTargets.TargetMod, "RestingObjectsParticipantData"));
        ObjectVisibility[] looks =
        [
            ObjectVisibility.Visible,
            ObjectVisibility.Invisible(InvisibleObjectKind.IdleMarkers),
            ObjectVisibility.Invisible(InvisibleObjectKind.MapMarkers),
            ObjectVisibility.Visible,
            ObjectVisibility.Visible,
        ];
        var targets = looks
            .Select((look, index) => TestTargets.Create(index, TestTargets.At(default), TestVisibility.TaggedBase(look), TestTargets.Space))
            .ToList();
        var meshPaths = new TargetMeshPaths(["a.nif", "idle.nif", "map.nif", null, "b.nif"]);

        var excluded = TouchSearch.MarkExcluded(targets, shapes, excluded: [4], meshPaths);

        Assert.Equal(new[] { false, true, true, true, true }, excluded);
    }

    [Fact]
    public void AskingForATargetThatDoesNotExistFails()
    {
        var meshPaths = new TargetMeshPaths(["a.nif", null]);

        Assert.True(meshPaths.HasMesh(0));
        Assert.False(meshPaths.HasMesh(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => meshPaths.HasMesh(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => meshPaths.HasMesh(-1));
    }
}
