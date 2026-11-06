using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>The supporters an object's support is measured against in a world, each in the order the world gives them.</summary>
/// <param name="around">Those its contacts are found among.</param>
/// <param name="below">Those its drop is measured to.</param>
internal sealed class SupportersRead(List<MeshSupporter> around, List<MeshSupporter> below)
{
    private readonly List<MeshSupporter> _around = around;
    private readonly List<MeshSupporter> _below = below;

    /// <summary>Whether both list the same supporters in the same order, each at a placement equal to the bit, so every measurement against them is too.</summary>
    public bool IsSameAs(SupportersRead other) => AreSame(_around, other._around) && AreSame(_below, other._below);

    private static bool AreSame(List<MeshSupporter> first, List<MeshSupporter> second)
    {
        if (first.Count != second.Count) return false;
        for (var k = 0; k < first.Count; k++)
        {
            if (!IsSame(first[k], second[k])) return false;
        }
        return true;
    }

    private static bool IsSame(MeshSupporter first, MeshSupporter second) =>
        first.Supporter == second.Supporter && first.MeshPath == second.MeshPath && first.Transform.IsSameToTheBitAs(second.Transform);
}

/// <summary>
/// The one test of whether an object loses its support when objects are taken away or changed, for other mods' objects and target
/// objects alike: it rested on what was below it before, it lost at least the threshold share of the weight of its surface
/// touching anything, lost = 1 - (weight touching after) / (weight touching before), and it can move down by more than the gap
/// after. A contact point that also touches the terrain or an object that stays still touches something, so it is not lost.
/// The share is checked first, as it costs far less than the drops. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class SupportLoss(OtherObjectSupport support, DropFinder drops, float threshold)
{
    /// <returns>The weight of the object's surface touching anything in the world.</returns>
    public float FindWeightBefore(StandingObject standing, SupportWorld world, OtherObjectQueries queries) =>
        support.FindContacts(standing, world, queries).TotalWeight;

    /// <param name="weightBefore">The weight of its surface touching anything in <paramref name="before"/>.</param>
    /// <returns>The share of its support the object lost in <paramref name="after"/>, when it loses its support; null otherwise.</returns>
    public float? FindLostShare(StandingObject standing, float weightBefore, SupportWorld before, SupportWorld after, OtherObjectQueries queries)
    {
        if (!(weightBefore > 0f)) return null;
        var lost = FindLost(weightBefore, support.FindContacts(standing, after, queries));
        return support.ReachesThreshold(lost, threshold) && drops.Rests(standing, before, queries) && !drops.Rests(standing, after, queries) ? lost : null;
    }

    /// <summary>
    /// What <see cref="FindLostShare"/> reads of its world after besides the ground: two worlds after on the same ground in which the
    /// object reads the same supporters give it the same lost share.
    /// </summary>
    public SupportersRead FindSupportersRead(StandingObject standing, SupportWorld after, OtherObjectQueries queries) =>
        new(support.FindSupporters(standing, after, queries), DropFinder.FindSupportersBelow(standing, after, queries));

    /// <summary>The target objects in <paramref name="world"/> that the object loses its support without.</summary>
    /// <returns>
    /// Each target object it touches that it loses its support without, taken out alone; when there is none, every target object
    /// it touches, when it loses its support without them taken out together; otherwise none.
    /// </returns>
    public List<TargetId> FindTargetsHoldingUp(StandingObject standing, SupportWorld world, OtherObjectQueries queries)
    {
        var hits = support.FindHits(standing, world, queries);
        var contacts = support.ContactsOf(hits);
        List<TargetId> touched = [.. contacts.Supporters.Where(entry => entry.Supporter.Type == SupporterType.Target).Select(entry => new TargetId(entry.Supporter.Index))];
        if (touched.Count == 0) return [];

        List<TargetId> reachingAlone = [.. touched.Where(target => ReachesThreshold(contacts.TotalWeight, support.ContactsWithout(hits, [target])))];
        var reachingTogether = ReachesThreshold(contacts.TotalWeight, support.ContactsWithout(hits, touched));
        if ((reachingAlone.Count == 0 && !reachingTogether) || !drops.Rests(standing, world, queries)) return [];

        List<TargetId> alone = [.. reachingAlone.Where(target => !drops.Rests(standing, world.Without([target]), queries))];
        if (alone.Count > 0) return alone;
        return reachingTogether && !drops.Rests(standing, world.Without(touched), queries) ? touched : [];
    }

    private bool ReachesThreshold(float weightBefore, CandidateContacts after) => support.ReachesThreshold(FindLost(weightBefore, after), threshold);

    private static float FindLost(float weightBefore, CandidateContacts after) => 1f - after.TotalWeight / weightBefore;
}
