using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Leftover invisible object selectors over in-memory targets and other mods' objects in <see cref="TestTargets.Space"/>.</summary>
internal static class TestLeftovers
{
    private static readonly ModKey ReachRecords = ModKey.FromNameAndExtension("Reach.esp");

    /// <param name="others">Other mods' objects of <see cref="TestTargets.Space"/>.</param>
    public static LeftoverInvisibleObjectSelector CreateSelector(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        BaseObjectShapeProvider shapes,
        IReadOnlyList<OtherObject> others,
        Protection protection,
        LeftoverConfig config) =>
        new(
            targets,
            visibility,
            shapes,
            FindHosts(targets, visibility, shapes, others),
            new InvisibleObjectReach(new SkyrimMod(ReachRecords, SkyrimRelease.SkyrimSE).ToImmutableLinkCache(), shapes),
            protection,
            config);

    private static Hosts FindHosts(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        BaseObjectShapeProvider shapes,
        IReadOnlyList<OtherObject> others)
    {
        var indexes = new Dictionary<FormKey, OtherObjectIndex>
        {
            [TestTargets.Space] = OtherObjectIndex.CreateUncounted(others, shapes, new ParallelOptions()),
        };
        var containment = new ObjectContainment(shapes, new TriangleTreeCache(shapes.ReadGeometry));
        return Hosts.Find(targets, visibility, indexes, containment, Replacements.None(others.Count), new ParallelOptions());
    }
}
