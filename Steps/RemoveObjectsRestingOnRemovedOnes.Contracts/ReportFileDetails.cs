using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>Why the touch cascade removed what it removed: its chains and every touching edge inside them.</summary>
/// <param name="Edges">Every touching pair with both ends, held objects included, in the same chain.</param>
public sealed record TouchChainEdges(TouchChainSet Components, ImmutableArray<TouchEdge> Edges);

/// <summary>Where a target mesh's origin sits inside the bounds of its triangles.</summary>
/// <param name="BaseEditorIds">The Editor IDs of the target bases using the mesh.</param>
/// <param name="References">The target objects using the mesh.</param>
/// <param name="TriangleBounds">Local bounds of the mesh's triangles.</param>
/// <param name="OriginFractions">Per axis: 0 at the minimum, 1 at the maximum; NaN on an axis without size.</param>
public sealed record MeshOrigin(
    string Mesh,
    ImmutableArray<string> BaseEditorIds,
    int References,
    Box TriangleBounds,
    Vector3 OriginFractions,
    MeshOriginPlace Class);

/// <summary>Where a mesh's origin sits inside the bounds of its triangles.</summary>
public enum MeshOriginPlace { NearBottom, NearCentre, Other }

/// <summary>The costly evidence only report files need.</summary>
/// <param name="Outcome">
/// Switched off when no report files are written or the step is switched off; stopped by an earlier failure when the
/// step's rounds or its touch chains failed; stopped by an unexpected error when the computation itself failed; otherwise performed.
/// </param>
/// <param name="Touch">Present exactly when performed after EverythingTouchingIt rounds with seeds.</param>
/// <param name="MeshOrigins">Present exactly when performed after OnlyWhatLosesItsSupport rounds with seeds; by mesh path.</param>
/// <param name="Failure">The computation's own unexpected failure; null otherwise, also when the rounds or the touch chains failed, whose error line already printed.</param>
public sealed record ReportFileDetails(StepOutcome Outcome, TouchChainEdges? Touch, ImmutableArray<MeshOrigin>? MeshOrigins, PartFailure? Failure)
{
    /// <summary>Computed, and nothing to show.</summary>
    public static ReportFileDetails None { get; } = new(StepOutcome.Performed, null, null, null);

    /// <summary>Not computed because the step's rounds or touch chains failed; that failure has its own error line, so there is no second one.</summary>
    public static ReportFileDetails StoppedByEarlierFailure { get; } = new(StepOutcome.StoppedByEarlierFailure, null, null, null);

    public static ReportFileDetails SwitchedOff { get; } = new(StepOutcome.SwitchedOff, null, null, null);
}
