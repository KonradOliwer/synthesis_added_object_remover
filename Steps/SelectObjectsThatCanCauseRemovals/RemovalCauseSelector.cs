using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;

/// <summary>
/// Step 3: from the other mods' objects, finds the ones a target object replaces and the ones that can cause removals.
/// The measurement rules of the object caches belong to this step; the runner passes them to the caches.
/// </summary>
public static class RemovalCauseSelector
{
    private static readonly ObjectMeasurement Invisible = new(IsVisible: false, Center: default);

    /// <param name="OtherModObjects">The objects added by other mods.</param>
    public sealed record Input(
        CollectedObjects World, IOtherModObjectsBySpace OtherModObjects, IBaseObjectShapes Shapes, SelectionSettings Settings, Execution Execution);

    public static SelectedObjects Select(Input input)
    {
        var (world, otherModObjects) = (input.World, input.OtherModObjects);
        var replacements = ReplacementMatcher.Find(
            world.Targets, new OtherModObjectPositions(otherModObjects), world.OtherModObjects.Length, input.Shapes, world.Order, input.Execution);
        return new SelectedObjects(replacements, ViewsOf(otherModObjects, replacements), input.Settings.Npcs);
    }

    /// <summary>The too-close step's question about worlds other than the run's (see <see cref="SelectForOldPlace"/>).</summary>
    /// <param name="worldCaches">The object caches of the worlds asked about.</param>
    public static ICausesOfWorlds CreateCausesOfWorlds(IObjectCachesOfWorlds worldCaches) => new CausesOfWorlds(worldCaches);

    /// <summary>
    /// The views for objects standing in another world than the run's: every object of <paramref name="otherModObjects"/> can cause
    /// removals except <paramref name="excluded"/>, such as the versions of the records being checked that those objects replace.
    /// </summary>
    /// <param name="otherModObjects">The objects of the other world.</param>
    /// <param name="otherModObjectCount">The number of objects in <paramref name="otherModObjects"/>.</param>
    internal static SelectedObjects SelectForOldPlace(
        IOtherModObjectsBySpace otherModObjects, int otherModObjectCount, IEnumerable<OtherId> excluded, NpcHandling npcs)
    {
        var replacements = Replacements.Excluding(otherModObjectCount, excluded);
        return new SelectedObjects(replacements, ViewsOf(otherModObjects, replacements), npcs);
    }

    private static Dictionary<NpcHandling, IObjectsThatCanCauseRemovals> ViewsOf(IOtherModObjectsBySpace otherModObjects, Replacements replacements) =>
        Enum.GetValues<NpcHandling>().ToDictionary(
            npcs => npcs, npcs => (IObjectsThatCanCauseRemovals)new ObjectsThatCanCauseRemovals(otherModObjects, replacements, npcs));

    /// <summary>
    /// The other mods' objects count an object that cannot be measured as invisible, so it causes no removals; the failure is
    /// recorded in <paramref name="unexpectedErrors"/> under this step's part. The objects of any plugin let that failure end the run.
    /// </summary>
    public static ObjectMeasurementRules MeasurementRules(IUnexpectedErrors unexpectedErrors) => MeasurementRules(unexpectedErrors, SelectionParts.RemovalCauseSelection);

    /// <summary>As <see cref="MeasurementRules(IUnexpectedErrors)"/>, with the failure recorded under the part of the step whose checks the objects serve.</summary>
    public static ObjectMeasurementRules MeasurementRules(IUnexpectedErrors unexpectedErrors, PartKey failurePart) =>
        new(MeasureCountingFailuresAsInvisible(unexpectedErrors, failurePart), Measure);

    /// <summary>An object is visible by <see cref="IBaseObjectShapes.VisibilityOf"/>; its centre is the world-space centre of its bounds, or its position when that is not finite.</summary>
    private static ObjectMeasurement Measure(IBaseObjectShapes shapes, OtherObject other)
    {
        if (!shapes.VisibilityOf(other.Base, other.IsPrimitive, other.HasMapMarker).IsVisible) return Invisible;

        var centre = Boxes.WorldBoundsCenter(shapes.Of(other.Base).Box, other.Transform);
        return new ObjectMeasurement(IsVisible: true, Center: Vectors.IsFinite(centre) ? centre : other.Position);
    }

    private static Func<IBaseObjectShapes, OtherObject, ObjectMeasurement> MeasureCountingFailuresAsInvisible(IUnexpectedErrors unexpectedErrors, PartKey failurePart) =>
        (shapes, other) =>
        {
            try
            {
                return Measure(shapes, other);
            }
            catch (Exception ex) when (Failures.IsRecoverable(ex))
            {
                unexpectedErrors.Add(new PartFailure(failurePart, Failures.DescribeFailure(ex), new FailureSubject(other.Key.ToString(), other.EditorId, other.Id.Index)));
                return Invisible;
            }
        };
}
