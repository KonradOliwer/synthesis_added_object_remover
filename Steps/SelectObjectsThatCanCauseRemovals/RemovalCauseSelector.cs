using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;

/// <summary>
/// From the other mods' objects, finds the ones a target object replaces and the ones that can cause removals, by the rule of
/// which other mods' objects can cause removals. The measurement rules of the object caches belong to this step.
/// </summary>
public static class RemovalCauseSelector
{
    private static readonly ObjectMeasurement Invisible = new(IsVisible: false, Center: default);

    /// <param name="OtherModObjects">The objects added by other mods.</param>
    /// <param name="Causes">The rule that selects the causes in the run's world.</param>
    public sealed record Input(CollectedObjects World, IOtherModObjectsBySpace OtherModObjects, ICausesOfTheRun Causes, SelectionSettings Settings);

    public static SelectedObjects Select(Input input) => input.Causes.Select(input.World, input.OtherModObjects, input.Settings.Npcs);

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
