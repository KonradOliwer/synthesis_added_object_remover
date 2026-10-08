using AddedObjectRemover.Steps.BuildLogAndReports;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps;

/// <summary>Turns an unexpected failure for one placed object into an error the log groups with the others of its kind.</summary>
internal static class ObjectErrors
{
    public static UnexpectedError ForTarget(TargetFailure failure, TargetObject target, string part, string consequence) =>
        new(part, failure.Failure, consequence, new ErrorSubject(SubjectKind.Object, RecordNames.Describe(target), target.Id.Index));

    public static UnexpectedError ForOtherModObject(OtherObject other, string part, string consequence, Exception ex) =>
        Failures.Unexpected(part, consequence, ex, new ErrorSubject(SubjectKind.Object, RecordNames.Describe(other), other.Id.Index));
}
