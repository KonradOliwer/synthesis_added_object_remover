using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// The evidence the also-remove step's report-file details hold. Evidence the step promises
/// (<see cref="RestingObjectsResult.DetailsHoldTouch"/>, <see cref="RestingObjectsResult.DetailsHoldMeshOrigins"/>) but the details lack is
/// a bug in the step, so it throws.
/// </summary>
internal static class ExplanationEvidence
{
    public static TouchChainEdges Touch(RestingObjectsResult alsoRemove) =>
        alsoRemove.ReportFileDetails.Touch ?? throw new InvalidOperationException("The report-file details were performed but hold no touch edges.");

    public static ImmutableArray<MeshOrigin> MeshOrigins(RestingObjectsResult alsoRemove) =>
        alsoRemove.ReportFileDetails.MeshOrigins ?? throw new InvalidOperationException("The report-file details were performed but hold no mesh origins.");
}
