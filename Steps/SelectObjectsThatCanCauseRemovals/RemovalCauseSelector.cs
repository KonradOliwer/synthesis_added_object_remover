using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;

/// <summary>
/// Step 3: from the other mods' objects, finds the ones a target object replaces and the ones that can cause removals.
/// The measurement rules of the object caches belong to this step; the runner passes them to the caches.
/// </summary>
public static class RemovalCauseSelector
{
    private static readonly ObjectMeasurement Invisible = new(IsVisible: false, Center: default);

    /// <param name="otherModObjects">C9.</param>
    public static SelectedObjects Select(
        CollectedObjects world, IOtherModObjectsBySpace otherModObjects, IBaseObjectShapes shapes, SelectionSettings settings, Execution execution)
    {
        var replacements = ReplacementMatcher.Find(
            world.Targets, new OtherModObjectPositions(otherModObjects), world.OtherModObjects.Length, shapes, world.Order, execution);
        return new SelectedObjects(replacements, ViewsOf(otherModObjects, replacements), settings.Npcs);
    }

    /// <summary>
    /// The views for an earlier version standing at its old place: every object of <paramref name="otherModObjects"/> can cause
    /// removals except <paramref name="excluded"/>, the cleaned mod's own version of the record being checked, which the earlier
    /// version replaces.
    /// </summary>
    /// <param name="otherModObjects">The objects of the old-place world (C9).</param>
    /// <param name="otherModObjectCount">The number of objects in <paramref name="otherModObjects"/>.</param>
    public static SelectedObjects SelectForOldPlace(
        IOtherModObjectsBySpace otherModObjects, int otherModObjectCount, OtherId excluded, NpcHandling npcs)
    {
        var replacements = Replacements.Excluding(otherModObjectCount, excluded);
        return new SelectedObjects(replacements, ViewsOf(otherModObjects, replacements), npcs);
    }

    private static Dictionary<NpcHandling, IObjectsThatCanCauseRemovals> ViewsOf(IOtherModObjectsBySpace otherModObjects, Replacements replacements) =>
        Enum.GetValues<NpcHandling>().ToDictionary(
            npcs => npcs, npcs => (IObjectsThatCanCauseRemovals)new ObjectsThatCanCauseRemovals(otherModObjects, replacements, npcs));

    /// <summary>
    /// The other mods' objects count an object that cannot be measured as invisible, so it causes no removals; the failure is
    /// recorded in <paramref name="unexpectedErrors"/>. The objects of any plugin let that failure end the run.
    /// </summary>
    public static ObjectMeasurementRules MeasurementRules(IUnexpectedErrors unexpectedErrors) =>
        new(MeasureCountingFailuresAsInvisible(unexpectedErrors), Measure);

    /// <summary>An object is visible by <see cref="IBaseObjectShapes.VisibilityOf"/>; its centre is the world-space centre of its bounds, or its position when that is not finite.</summary>
    private static ObjectMeasurement Measure(IBaseObjectShapes shapes, OtherObject other)
    {
        if (!shapes.VisibilityOf(other.Base, other.IsPrimitive, other.HasMapMarker).IsVisible) return Invisible;

        var centre = Boxes.WorldBoundsCenter(shapes.Of(other.Base).Box, other.Transform);
        return new ObjectMeasurement(IsVisible: true, Center: Vectors.IsFinite(centre) ? centre : other.Position);
    }

    private static Func<IBaseObjectShapes, OtherObject, ObjectMeasurement> MeasureCountingFailuresAsInvisible(IUnexpectedErrors unexpectedErrors) =>
        (shapes, other) =>
        {
            try
            {
                return Measure(shapes, other);
            }
            catch (Exception ex) when (Failures.IsRecoverable(ex))
            {
                unexpectedErrors.Add(new PartFailure(
                    SelectionParts.RemovalCauseSelection, Failures.DescribeFailure(ex), new FailureSubject(other.Key.ToString(), other.EditorId, other.Id.Index)));
                return Invisible;
            }
        };
}
