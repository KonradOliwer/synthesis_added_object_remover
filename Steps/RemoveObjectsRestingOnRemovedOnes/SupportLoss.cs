using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>What an object had in the world before a change.</summary>
/// <param name="Weight">The weight of its surface touching anything.</param>
/// <param name="Rested">Whether it rested on what was below it.</param>
internal readonly record struct SupportBefore(float Weight, bool Rested);

/// <summary>
/// The one test of whether an object loses its support when objects are taken away or changed, for other mods' objects and target
/// objects alike: it rested on what was below it before, it lost at least the threshold share of the weight of its surface
/// touching anything, lost = 1 - (weight touching after) / (weight touching before), and it can move down by more than the gap
/// after. A contact point that also touches the terrain or an object that stays still touches something, so it is not lost.
/// Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class SupportLoss(OtherObjectSupport support, DropFinder drops, float threshold)
{
    public SupportBefore FindSupportBefore(StandingObject standing, SupportWorld world, OtherObjectQueries queries) =>
        new(support.FindContacts(standing, world, queries).TotalWeight, drops.Rests(standing, world, queries));

    /// <returns>The share of its support the object lost in <paramref name="after"/>, when it loses its support; null otherwise.</returns>
    public float? FindLostShare(StandingObject standing, SupportBefore before, SupportWorld after, OtherObjectQueries queries)
    {
        if (!(before.Weight > 0f) || !before.Rested) return null;
        var lost = 1f - support.FindContacts(standing, after, queries).TotalWeight / before.Weight;
        return SupportRule.ReachesThreshold(lost, threshold) && !drops.Rests(standing, after, queries) ? lost : null;
    }

    /// <summary>The target objects in <paramref name="world"/> that the object loses its support without.</summary>
    /// <returns>
    /// Each target object it touches that it loses its support without, taken out alone; when there is none, every target object
    /// it touches, when it loses its support without them taken out together; otherwise none.
    /// </returns>
    public List<TargetId> FindTargetsHoldingUp(StandingObject standing, SupportWorld world, OtherObjectQueries queries)
    {
        var contacts = support.FindContacts(standing, world, queries);
        List<TargetId> touched = [.. contacts.Supporters.Where(entry => entry.Supporter.Type == SupporterType.Target).Select(entry => new TargetId(entry.Supporter.Index))];
        if (touched.Count == 0) return [];

        var before = new SupportBefore(contacts.TotalWeight, drops.Rests(standing, world, queries));
        List<TargetId> alone = [.. touched.Where(target => FindLostShare(standing, before, world.Without([target]), queries) is not null)];
        if (alone.Count > 0) return alone;
        return FindLostShare(standing, before, world.Without(touched), queries) is not null ? touched : [];
    }
}
