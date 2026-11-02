using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;

/// <summary>The support test asked about objects that move away, in worlds of what stays (see <see cref="Find"/>).</summary>
/// <param name="worldCaches">The object caches of the worlds asked about.</param>
internal sealed class HoldersOfObjectsThatStay(IObjectCachesOfWorlds worldCaches, OtherModsObjects.SupportJudging judging) : IHoldersOfObjectsThatStay
{
    public float TouchGap => judging.Settings.TouchGap;

    public IHoldersSearch Over(CollectedObjects world, IReadOnlyList<TargetObject> standingNow) => new Search(this, world, standingNow);

    private IObjectCaches CachesOf(CollectedObjects world) => worldCaches.Of(world);

    /// <summary>
    /// The objects that move away from where they stand now and that an object that stays rests on there, by the test that finds the
    /// other mods' objects left without support. Every object of <paramref name="staying"/> that can rest counts, whichever plugin it
    /// belongs to.
    /// </summary>
    /// <param name="holders">Where they stand now; the id of each is its position.</param>
    /// <param name="holderIndex">The index of <paramref name="holders"/>.</param>
    /// <param name="movesAway">Which holders move away; the others stay, as their copies in <paramref name="staying"/>.</param>
    /// <param name="staying">The visible objects of any plugin as they stand once the run is done, with a copy of each holder.</param>
    /// <param name="copiesOfMoving">The copies in <paramref name="staying"/> of the holders that move away, which neither rest nor hold up.</param>
    /// <returns>Each holder that moves away and holds up an object that stays, with the first such object in id order.</returns>
    private IReadOnlyDictionary<TargetId, OtherObject> Find(
        IReadOnlyList<TargetObject> holders,
        IVisibleTargetObjects holderIndex,
        Func<TargetId, bool> movesAway,
        IVisibleObjectsOfAnyPlugin staying,
        IReadOnlySet<OtherId> copiesOfMoving)
    {
        var world = new SupportWorld(holders, holderIndex, movesAway, staying, copiesOfMoving.ToDictionary(id => id, _ => (PlacedTransform?)null), judging.Caches.Shapes);
        var rules = judging.CreateRules();
        return judging.CreateHolding(world, staying, rules, rules.CanRest).Find(holders.Where(holder => movesAway(holder.Id)), copiesOfMoving.Contains);
    }

    /// <summary>The index of the objects standing now and the visible objects of the world are built on the first search, once.</summary>
    private sealed class Search(HoldersOfObjectsThatStay test, CollectedObjects world, IReadOnlyList<TargetObject> standingNow) : IHoldersSearch
    {
        private readonly ComputedOnce<IVisibleTargetObjects> _standingIndex = new(() => test.CachesOf(world).IndexVisibleTargets(standingNow));
        private readonly ComputedOnce<IVisibleObjectsOfAnyPlugin> _staying = new(() => test.CachesOf(world).VisibleObjectsOfAnyPlugin());

        public IReadOnlyDictionary<TargetId, OtherObject> Find(Func<TargetId, bool> movesAway, IReadOnlySet<OtherId> copiesOfMoving) =>
            test.Find(standingNow, _standingIndex.Value, movesAway, _staying.Value, copiesOfMoving);
    }
}
