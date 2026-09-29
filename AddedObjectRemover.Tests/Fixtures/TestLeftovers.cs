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
        LeftoverConfig config)
    {
        var scene = TestScenes.Create(targets, others, shapes);
        var order = WorkOrder.Of(targets);
        return new(
            targets,
            visibility,
            scene.VisibleTargets(visibility),
            Hosts.Find(targets, visibility, scene.ActiveRivals(Replacements.None(others.Count), NpcHandling.OnlyWhenStuckInObject), order, new ParallelOptions()),
            new InvisibleObjectReach(new BaseFactsReader(new SkyrimMod(ReachRecords, SkyrimRelease.SkyrimSE).ToImmutableLinkCache())),
            config,
            order);
    }

    /// <summary>The leftover round applied to a fresh ledger, so referenced objects are held.</summary>
    public static LeftoverResult DecideWithLedger(LeftoverResult evaluated, Protection protection, int targetCount) =>
        evaluated.WithVerdicts(Ledger.Start(protection, targetCount).Apply(RoundKind.Leftover, evaluated.Proposals));
}
