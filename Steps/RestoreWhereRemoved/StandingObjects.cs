using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The objects that stay in the final world and block actors (<see cref="ObjectsThatBlockActors"/>), as boxes: the after-removal
/// world's objects that are a change there (another mod's object the game shows differently from its accepted values) but for the placed records that go back, and the placed records whose areas let
/// them go back but a guard keeps, where they stand now. An object that ends removed in the final world (<see cref="FinalPresence"/>),
/// such as a kept record the cleaned mod disabled or deleted, stands nowhere, so it is none of them.
/// </summary>
internal sealed class StandingObjects
{
    private readonly AfterRemovalWorld _world;
    private readonly FinalPresence _presence;
    private readonly ImmutableArray<StandingBox> _changes;

    /// <summary>Each candidate that stands now, where it stands, by its position among them; null for one actors would not run into.</summary>
    private readonly ImmutableArray<StandingBox?> _now;

    public StandingObjects(AfterRemovalWorld world, ObjectsThatBlockActors blocking, FinalPresence presence)
    {
        _world = world;
        _presence = presence;
        _changes = [.. world.Causes.Select(other => BoxOf(other.Key, other.SpaceKey, blocking.SolidBoxOf(other))).OfType<StandingBox>()];
        _now = [.. world.Now.Select(placed => BoxOf(placed.Key, placed.SpaceKey, blocking.SolidBoxOf(placed)))];
    }

    /// <param name="goingBack">The indexes of the placed records that go back among those whose areas let them.</param>
    /// <returns>In key order, each object once.</returns>
    public List<StandingBox> ThatStay(IReadOnlySet<int> goingBack)
    {
        var moving = goingBack.Select(index => _world.Restored[index].Key).ToHashSet();
        var kept = _world.StandingNow.Select((candidate, position) => goingBack.Contains(candidate) ? null : _now[position]).OfType<StandingBox>();
        return
        [
            .. _changes.Where(standing => !moving.Contains(standing.Key)).Concat(kept)
                .DistinctBy(standing => standing.Key)
                .Where(standing => !_presence.EndsRemoved(standing.Key, goingBack.Contains))
                .OrderBy(standing => standing.Key, RecordKeyOrder.Comparer),
        ];
    }

    private static StandingBox? BoxOf(RecordKey key, RecordKey spaceKey, OrientedBox? box) => box is { } solid ? new StandingBox(key, spaceKey, solid) : null;
}
