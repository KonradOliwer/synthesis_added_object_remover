using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The objects of other plugins, and the cleaned mod's objects later plugins hold, that the ground going back up would bury and that
/// were placed for the ground as it is. Only visible objects, not placed NPCs or creatures, whose creating plugin and winning plugin
/// past the patches are both trusted in full (the cleaned mod included) and that the game shows otherwise than before the cleaned mod
/// count: a later version or the patch's pull-down placed them,
/// while one shown as before the cleaned mod stood on the ground going back. Ground going back up buries an object where, at a place
/// under its box, the planned ground lies more than the height tolerance above both its box's bottom and the ground as it is. It was
/// placed for the ground as it is when, over the places under its box where the ground goes up, its bottom lies as close to the lowest
/// ground as it is as to the lowest planned ground, or closer; one placed for the ground going back gains nothing from keeping the
/// ground as it is. Places where the ground stays are left out of that comparison: they hold the same height in both, so an object
/// reaching past the ground going up would always count as placed for the ground as it is. Thread-safe.
/// </summary>
internal sealed class ObjectsBuriedByGroundGoingUp
{
    private readonly ITerrainHeights _now;
    private readonly float _heightTolerance;

    /// <summary>The objects that count, each with its box's world bounds.</summary>
    private readonly ImmutableArray<(OtherObject Object, Box Bounds)> _shownOtherwiseThanBefore;

    /// <param name="standing">
    /// The objects of other plugins and the cleaned mod's objects later plugins hold that stand where they stand now in the final world,
    /// in the order to report.
    /// </param>
    /// <param name="pulledDown">The keys of the objects the patch pulls down.</param>
    /// <param name="accepted">The world the cleaned mod's objects are judged in, whose fully trusted plugins are those trusted in full.</param>
    /// <param name="now">The ground as it is.</param>
    public ObjectsBuriedByGroundGoingUp(
        IEnumerable<OtherObject> standing, IReadOnlySet<RecordKey> pulledDown, AcceptedWorld accepted, IBaseObjectShapes shapes, ITerrainHeights now, float heightTolerance)
    {
        _now = now;
        _heightTolerance = heightTolerance;
        _shownOtherwiseThanBefore =
        [
            .. standing
                .Where(other => (other.ChangeSinceBeforeCleanedMod is not null || pulledDown.Contains(other.Key))
                    && !other.IsPlacedNpc
                    && shapes.VisibilityOf(other).IsVisible
                    && accepted.TrustOf(other.Key.Plugin) == VersionTrust.Full
                    && accepted.TrustOf(other.WinningModPastPatches) == VersionTrust.Full)
                .Select(other => (other, BaseBoxes.Of(shapes, other.Base, other.Transform).WorldAabb(0f))),
        ];
    }

    /// <param name="planned">The ground of the world after.</param>
    /// <returns>Where they stand now, in the order given.</returns>
    public IEnumerable<OtherObject> On(ITerrainHeights planned) =>
        _shownOtherwiseThanBefore.Where(entry => IsBuriedThoughPlacedForTheGroundAsItIs(entry.Object.SpaceKey, entry.Bounds, planned)).Select(entry => entry.Object);

    private bool IsBuriedThoughPlacedForTheGroundAsItIs(RecordKey space, Box bounds, ITerrainHeights planned)
    {
        if (!planned.HasTerrain(space) || !_now.HasTerrain(space)) return false;
        var goingUp = HeightsUnder(space, bounds, planned).Where(height => height.Planned - height.Now > _heightTolerance).ToList();
        return goingUp.Any(height => height.Planned - bounds.Min.Z > _heightTolerance)
            && MathF.Abs(bounds.Min.Z - goingUp.Min(height => height.Now)) <= MathF.Abs(bounds.Min.Z - goingUp.Min(height => height.Planned));
    }

    /// <summary>The ground as it is and the planned ground at each place under the box where both are known.</summary>
    private List<(float Now, float Planned)> HeightsUnder(RecordKey space, Box bounds, ITerrainHeights planned) =>
    [
        .. TerrainGrid.PlacesUnder(bounds).SelectMany(place =>
            planned.TryGetHeight(space, place, out var after) && _now.TryGetHeight(space, place, out var before) ? [(before, after)] : Array.Empty<(float, float)>()),
    ];
}
