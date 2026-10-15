using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// Which evidence the also-remove step's report-file details hold, decided from their status, the step's mode and
/// whether it had seeds. Evidence the status promises but the details lack is a bug in the step, so it throws.
/// </summary>
internal static class ExplanationEvidence
{
    public static bool HasTouch(RestingObjectsResult alsoRemove) => IsPromised(alsoRemove, FollowUpRemovalMode.EverythingTouching);

    public static TouchChainEdges Touch(RestingObjectsResult alsoRemove) =>
        alsoRemove.ReportFileDetails.Touch ?? throw new InvalidOperationException("The report-file details were performed but hold no touch edges.");

    public static bool HasMeshOrigins(RestingObjectsResult alsoRemove) => IsPromised(alsoRemove, FollowUpRemovalMode.ObjectsSupportedByIt);

    public static ImmutableArray<MeshOrigin> MeshOrigins(RestingObjectsResult alsoRemove) =>
        alsoRemove.ReportFileDetails.MeshOrigins ?? throw new InvalidOperationException("The report-file details were performed but hold no mesh origins.");

    private static bool IsPromised(RestingObjectsResult alsoRemove, FollowUpRemovalMode mode) =>
        alsoRemove.ReportFileDetails.Outcome == StepOutcome.Performed && alsoRemove.Mode == mode && alsoRemove.HadSeeds;
}
