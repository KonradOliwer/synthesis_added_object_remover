using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>One of the cleaned mod's objects an area counts.</summary>
/// <param name="Volume">What it adds to the area's size: its box volume; nothing for an object whose mesh holds only effect shapes.</param>
internal readonly record struct CountedObject(TargetId Target, double Volume);

/// <summary>
/// The cleaned mod's own objects the game draws in each column an area is measured in: those whose box centre lies within the radius
/// horizontally, at any height. What a column holds does not depend on any decision, so each column is found once and serves both
/// the area's judging and the search for its leftovers. Thread-safe.
/// </summary>
/// <param name="targets">Every target, by id.</param>
/// <param name="shownTargets">The shown targets where the rounds measure them.</param>
internal sealed class AreaContents(ImmutableArray<TargetObject> targets, IVisibleTargetObjects shownTargets, IBaseObjectShapes shapes)
{
    private readonly ComputedOncePerKey<(RecordKey Space, Vector2 Centre), ImmutableArray<CountedObject>> _columns =
        new(Publication.FirstWriteWins, EqualityComparer<(RecordKey, Vector2)>.Default);

    /// <param name="radius">The same for every ask of one column.</param>
    /// <returns>The cleaned mod's own objects in the column, in ascending id.</returns>
    public ImmutableArray<CountedObject> In(RecordKey space, Vector2 centre, float radius) => _columns.Get((space, centre), () => Find(space, centre, radius));

    private ImmutableArray<CountedObject> Find(RecordKey space, Vector2 centre, float radius)
    {
        var inColumn = new List<VisibleTargetNear>();
        shownTargets.InColumn(space, centre, radius, inColumn);
        return
        [
            .. inColumn
                .Select(near => (Near: near, Target: targets[near.Target.Index]))
                .Where(found => found.Target.Origin == TargetOrigin.CleanedMod)
                .Select(found => new CountedObject(found.Near.Target, VolumeOf(found.Target, found.Near.Box))),
        ];
    }

    /// <remarks>An effect volume (fog, light rays) can be far larger than the solid pieces around it, so only its count weighs.</remarks>
    private double VolumeOf(TargetObject target, OrientedBox box) => shapes.VisibilityOf(target).EffectOnlyMesh ? 0d : box.Volume;
}
