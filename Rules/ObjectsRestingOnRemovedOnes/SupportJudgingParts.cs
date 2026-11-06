using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>The parts of the other-mods support rule, each made from what a <see cref="SupportJudging"/> holds.</summary>
internal static class SupportJudgingParts
{
    public static OtherModsObjectRules CreateRules(SupportJudging judging) => new(judging.Trust, judging.Plugins, judging.Caches.Shapes);

    public static OtherObjectSupport CreateSupport(SupportJudging judging) => new(judging.Contacts, judging.Settings.TouchGap);

    /// <param name="touchGap">The largest gap that counts as touching for the rule that asks.</param>
    public static DropFinder CreateDropFinder(SupportJudging judging, float touchGap) => new(judging.Contacts, judging.Caches.Triangles, touchGap);

    public static GroundChangeUnder CreateGroundChange(SupportJudging judging) => new(judging.Contacts, judging.Settings.TouchGap);

    public static SupportLoss CreateSupportLoss(SupportJudging judging) =>
        new(CreateSupport(judging), CreateDropFinder(judging, judging.Settings.TouchGap), judging.Settings.SupportLostFraction);

    public static JudgedObjectsNear CreateNear(SupportJudging judging, IVisibleObjectsOfAnyPlugin others, Func<OtherObject, bool> isJudged) =>
        new(others, isJudged, judging.Settings.TouchGap, judging.Execution);

    /// <param name="original">The world the judged objects' support before is measured in.</param>
    /// <param name="final">The world after, without the objects found left without support.</param>
    /// <param name="isJudged">Which objects of <paramref name="others"/> are judged.</param>
    /// <param name="before">The weights before, shared by every settling between the same <paramref name="original"/> and judged objects.</param>
    public static LeftWithoutSupport CreateLeftWithoutSupport(
        SupportJudging judging, SupportWorld original, SupportWorld final, IVisibleObjectsOfAnyPlugin others, OtherModsObjectRules rules, Func<OtherObject, bool> isJudged, WeightsBefore before) =>
        new(
            original,
            final,
            CreateNear(judging, others, isJudged),
            rules,
            CreateSupportLoss(judging),
            CreateDropFinder(judging, judging.Settings.TouchGap),
            judging.Settings,
            before,
            judging.Execution);

    /// <param name="world">The target objects and objects of any plugin the support is judged among.</param>
    /// <param name="isJudged">Which objects of <paramref name="others"/> are checked for resting on a target object.</param>
    public static TargetsHoldingUpOtherModsObjects CreateHolding(
        SupportJudging judging, SupportWorld world, IVisibleObjectsOfAnyPlugin others, OtherModsObjectRules rules, Func<OtherObject, bool> isJudged) =>
        new(world, CreateNear(judging, others, isJudged), rules, CreateSupportLoss(judging), judging.Execution);

    /// <summary>The search among every target object and object of any plugin where it stands before any decision.</summary>
    public static TargetsHoldingUpOtherModsObjects CreateHoldingBeforeAnyDecision(SupportJudging judging)
    {
        var others = judging.Caches.VisibleObjectsOfAnyPlugin();
        var rules = CreateRules(judging);
        return CreateHolding(
            judging, new SupportWorld(judging.Targets, judging.Caches.VisibleTargets, _ => true, others, SupportWorld.NoChanges, judging.Caches.Shapes, judging.Caches.Terrain), others, rules, rules.IsJudged);
    }
}
