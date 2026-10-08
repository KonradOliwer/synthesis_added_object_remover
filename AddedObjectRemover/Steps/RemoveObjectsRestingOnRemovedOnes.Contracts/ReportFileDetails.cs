using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>Why the touch cascade removed what it removed: its chains and every touching edge inside them.</summary>
/// <param name="Edges">Every touching pair with both ends, held objects included, in the same chain.</param>
public sealed record TouchChainEdges(TouchChainSet Components, ImmutableArray<TouchEdge> Edges);

/// <summary>Where a target mesh's origin sits inside the bounds of its triangles.</summary>
/// <param name="BaseEditorIds">The Editor IDs of the target bases using the mesh.</param>
/// <param name="References">The target objects using the mesh.</param>
/// <param name="TriangleBounds">Local bounds of the mesh's triangles.</param>
/// <param name="OriginFractions">Per axis: 0 at the minimum, 1 at the maximum; NaN on an axis without size.</param>
/// <param name="Class">Origin near bottom, near centre, or other.</param>
public sealed record MeshOrigin(
    string Mesh,
    ImmutableArray<string> BaseEditorIds,
    int References,
    Box TriangleBounds,
    Vector3 OriginFractions,
    string Class);

/// <summary>The costly evidence only report files need.</summary>
/// <param name="Touch">Only for EverythingTouching with seeds.</param>
/// <param name="MeshOrigins">Only for ObjectsSupportedByIt with seeds; by mesh path.</param>
public sealed record ReportFileDetails(TouchChainEdges? Touch, ImmutableArray<MeshOrigin>? MeshOrigins)
{
    public static ReportFileDetails None { get; } = new(null, null);
}
