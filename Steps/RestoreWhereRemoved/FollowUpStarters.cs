using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The candidates each object the follow-up took along went with: a leaving candidate it, or an object it went with, lost its support
/// from or touched; and whether ground going down is among them. What it went with that the run already changed starts nothing. An
/// object may go with another that went with it (two objects leaning on each other), so the links are followed as a graph.
/// </summary>
/// <param name="TargetsLeaving">The leaving candidate of each target candidate leaving.</param>
/// <param name="OthersLeaving">The leaving candidate of each other candidate leaving, by its id among the run's objects.</param>
/// <param name="TargetsWent">What each followed target object went with.</param>
/// <param name="OthersWent">What each object of any plugin the follow-up took along, left without support or found held up went with.</param>
internal sealed record FollowUpStarters(
    IReadOnlyDictionary<TargetId, int> TargetsLeaving,
    IReadOnlyDictionary<OtherId, int> OthersLeaving,
    IReadOnlyDictionary<TargetId, ImmutableArray<Supporter>> TargetsWent,
    IReadOnlyDictionary<OtherId, ImmutableArray<Supporter>> OthersWent)
{
    public static FollowUpStarters Of(FollowUpOutcomes outcomes, IReadOnlyDictionary<TargetId, int> targetsLeaving, IReadOnlyDictionary<OtherId, int> othersLeaving) =>
        new(
            targetsLeaving,
            othersLeaving,
            outcomes.Targets.ToDictionary(entry => entry.Target, entry => entry.LostFrom),
            outcomes.Copies.Select(entry => (entry.Object.Id, entry.LostFrom))
                .Concat(outcomes.Others.Select(entry => (entry.Outcome.Object.Id, entry.LostFrom)))
                .Concat(outcomes.HeldUp.Select(entry => (entry.Object.Id, entry.LostFrom)))
                .ToDictionary(entry => entry.Id, entry => entry.LostFrom));

    /// <param name="wentWith">What the object went with.</param>
    /// <param name="by">The object's key, for the message of a bug.</param>
    /// <returns>
    /// The candidates, and whether ground going down is among what it went with; at least one of the two: an object taken along
    /// without either is a bug.
    /// </returns>
    public Started StartersOf(ImmutableArray<Supporter> wentWith, RecordKey by)
    {
        var started = new Started([], []);
        var seen = new HashSet<Supporter>();
        var waiting = new Queue<(Supporter? Owner, Supporter Supporter)>(wentWith.Select(supporter => ((Supporter?)null, supporter)));
        while (waiting.TryDequeue(out var entry))
        {
            if (entry.Supporter == Supporter.Terrain)
            {
                if (!started.OnGround.Contains(entry.Owner)) started.OnGround.Add(entry.Owner);
                continue;
            }
            if (!seen.Add(entry.Supporter)) continue;
            if (Find(entry.Supporter, TargetsLeaving, OthersLeaving, out var candidate)) started.Candidates.Add(candidate);
            if (Find(entry.Supporter, TargetsWent, OthersWent, out var next)) foreach (var further in next) waiting.Enqueue((entry.Supporter, further));
        }
        if (started.Candidates.Count == 0 && !started.ByGround) throw new UnreachableException($"The follow-up of {by} goes back to no candidate leaving and no ground going down.");
        return started;
    }

    /// <param name="Candidates">The candidates leaving it goes back to.</param>
    /// <param name="OnGround">
    /// The objects among what it went with whose support ground going down took, in the order found: null for the object itself, then
    /// the supporters it went with.
    /// </param>
    public sealed record Started(HashSet<int> Candidates, List<Supporter?> OnGround)
    {
        /// <summary>Whether ground going down is among what it went with.</summary>
        public bool ByGround => OnGround.Count > 0;
    }

    /// <summary>The entry of a target supporter in <paramref name="ofTargets"/>, or of a placed one in <paramref name="ofOthers"/>.</summary>
    private static bool Find<T>(Supporter supporter, IReadOnlyDictionary<TargetId, T> ofTargets, IReadOnlyDictionary<OtherId, T> ofOthers, out T found)
    {
        found = default!;
        return supporter.Type switch
        {
            SupporterType.Target => ofTargets.TryGetValue(new TargetId(supporter.Index), out found!),
            SupporterType.PlacedObject => ofOthers.TryGetValue(new OtherId(supporter.Index), out found!),
            _ => false,
        };
    }
}
