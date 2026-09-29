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
        ShapeCatalog shapes,
        IReadOnlyList<OtherObject> others,
        LeftoverConfig config) =>
        new(
            targets,
            visibility,
            shapes,
            FindHosts(targets, visibility, shapes, others),
            new InvisibleObjectReach(new BaseFactsReader(new SkyrimMod(ReachRecords, SkyrimRelease.SkyrimSE).ToImmutableLinkCache())),
            config);

    /// <summary>The leftover round applied to a fresh ledger, so referenced objects are held.</summary>
    public static LeftoverResult DecideWithLedger(LeftoverResult evaluated, Protection protection, int targetCount) =>
        evaluated.WithVerdicts(Ledger.Start(protection, targetCount).Apply(RoundKind.Leftover, evaluated.Proposals));

    private static Hosts FindHosts(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        ShapeCatalog shapes,
        IReadOnlyList<OtherObject> others)
    {
        var indexes = new Dictionary<FormKey, OtherObjectIndex>
        {
            [TestTargets.Space] = OtherObjectIndex.CreateUncounted(others, shapes, new ParallelOptions()),
        };
        var containment = new ObjectContainment(shapes, new TriangleStore(shapes.ReadGeometry));
        return Hosts.Find(targets, visibility, indexes, containment, Replacements.None(others.Count), new ParallelOptions());
    }
}
