using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The guard of a door going back whose navmesh link goes with it (<see cref="DoorLinks"/>): its link must be consistent in the
/// final world. A link a later plugin changed again cannot go back. The navmesh the link names either is a member of a navmesh group
/// going back, which then judges the door with it (N3), or stands as it wins and passes the consistency check with the restores of
/// every door whose written link names it; and no navmesh the cleaned mod changed that stays as it wins names the door at another
/// triangle. Thread-safe.
/// </summary>
/// <remarks>
/// The doors whose written links name one navmesh go back together or not at all, as a navmesh group does with its doors: the navmesh
/// may name each of them, so it can be consistent only with all of them back.
/// </remarks>
/// <param name="candidates">The placed records whose areas let them go back, in key order, each with the parts its restore writes.</param>
/// <param name="changedNavmeshes">Every navmesh the cleaned mod changed or added.</param>
/// <param name="findFaultIfWritten">The consistency check's first fault over the world the orders alone would leave; null when none.</param>
internal sealed class DoorLinkGuard(
    IReadOnlyList<RecordChangedByCleanedMod> candidates,
    IEnumerable<NavmeshChangedByCleanedMod> changedNavmeshes,
    Func<IReadOnlyList<WriteOrder>, NavmeshFault?> findFaultIfWritten)
{
    /// <summary>For each door, the links the winning versions of the navmeshes the cleaned mod changed give it through their door triangles, in key order of the navmeshes.</summary>
    private readonly ILookup<RecordKey, NavDoorLinkFact> _namedByChanged = changedNavmeshes
        .Where(navmesh => !navmesh.Winner.IsDeleted)
        .OrderBy(navmesh => navmesh.Navmesh, RecordKeyOrder.Comparer)
        .SelectMany(navmesh => navmesh.Winner.DoorTriangles.Select(triangle => (triangle.Door, Link: new NavDoorLinkFact(navmesh.Navmesh, triangle.Triangle))))
        .ToLookup(entry => entry.Door, entry => entry.Link);

    /// <summary>The navmesh the written link of each candidate whose restore writes a link names, by candidate index.</summary>
    private readonly IReadOnlyDictionary<int, RecordKey> _navmeshOfWrittenLink =
        WrittenLinks(candidates).ToDictionary(written => written.Candidate, written => written.Navmesh);

    /// <summary>The candidates whose written links name each navmesh, in candidate order.</summary>
    private readonly ILookup<RecordKey, int> _doorsByNavmesh = WrittenLinks(candidates).ToLookup(written => written.Navmesh, written => written.Candidate);

    /// <summary>
    /// The consistency check's first fault over the restores, written together, of the candidates whose written links name one
    /// navmesh, for each such navmesh, one after another in key order of the navmeshes; null for one without a fault.
    /// </summary>
    public IReadOnlyDictionary<RecordKey, NavmeshFault?> CheckEachNavmeshWithItsDoors() =>
        _doorsByNavmesh
            .OrderBy(doors => doors.Key, RecordKeyOrder.Comparer)
            .ToDictionary(doors => doors.Key, doors => findFaultIfWritten([.. doors.Select(RestoreOf)]));

    /// <param name="goingBack">The indexes of the candidates still going back.</param>
    /// <param name="navmeshesGoingBack">The members of the navmesh groups going back.</param>
    /// <param name="navmeshFaults">What <see cref="CheckEachNavmeshWithItsDoors"/> found.</param>
    /// <returns>Null when the candidate is no such door or its link fits.</returns>
    public PlacedRefusal? Check(
        int candidate, IReadOnlySet<int> goingBack, IReadOnlySet<RecordKey> navmeshesGoingBack, IReadOnlyDictionary<RecordKey, NavmeshFault?> navmeshFaults)
    {
        var change = candidates[candidate];
        if (!DoorLinks.GoesWithPlacement(change)) return null;
        var door = change.DoorLink!;
        if (door.NavDoorLink is PartOutcome.LeftToLaterPlugin) return new PlacedRefusal(PlacedGuard.DoorLinkChangedLater, By: null, Failure: null);
        if (_navmeshOfWrittenLink.TryGetValue(candidate, out var named) && !navmeshesGoingBack.Contains(named) && CheckNavmeshThatStays(named, goingBack, navmeshFaults) is { } refusal)
        {
            return refusal;
        }

        var link = DoorLinks.LinkGoingBack(door);
        var elsewhere = _namedByChanged[change.Key].Where(other => !navmeshesGoingBack.Contains(other.Navmesh) && other != link).Select(other => (NavDoorLinkFact?)other).FirstOrDefault();
        return elsewhere is { } staying ? DoesNotMatch(staying.Navmesh) : null;
    }

    /// <summary>A door whose written link names the navmesh no longer goes back, or the navmesh is at fault with all of them back.</summary>
    private PlacedRefusal? CheckNavmeshThatStays(RecordKey navmesh, IReadOnlySet<int> goingBack, IReadOnlyDictionary<RecordKey, NavmeshFault?> navmeshFaults)
    {
        if (_doorsByNavmesh[navmesh].Any(door => !goingBack.Contains(door))) return DoesNotMatch(navmesh);
        return navmeshFaults[navmesh] is { } fault ? DoesNotMatch(fault.Navmesh) : null;
    }

    /// <summary>Each candidate whose restore writes a link, with the navmesh that link names, in candidate order; a written link that names none is left out.</summary>
    private static IEnumerable<(int Candidate, RecordKey Navmesh)> WrittenLinks(IReadOnlyList<RecordChangedByCleanedMod> candidates) =>
        Enumerable.Range(0, candidates.Count)
            .Where(candidate => candidates[candidate].Earlier.Undo.HasFlag(PlacedParts.NavDoorLink))
            .Select(candidate => (Candidate: candidate, Link: DoorLinks.LinkGoingBack(candidates[candidate].DoorLink!)))
            .Where(written => written.Link is not null)
            .Select(written => (written.Candidate, written.Link!.Value.Navmesh));

    private UndoPlacedParts RestoreOf(int candidate) => new(candidates[candidate].Key, candidates[candidate].Earlier.Undo);

    private static PlacedRefusal DoesNotMatch(RecordKey navmesh) => new(PlacedGuard.DoorLinkDoesNotMatch, navmesh, Failure: null);
}
