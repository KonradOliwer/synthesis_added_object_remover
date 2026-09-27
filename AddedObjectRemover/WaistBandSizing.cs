using System.Numerics;

namespace AddedObjectRemover;

/// <param name="Raw">Bounds of every vertex, arms spread as in the bind pose.</param>
/// <param name="Body">The standing body's box: <see cref="Raw"/>'s full height, width and depth from the waist band.</param>
internal readonly record struct SkinnedBodySize(Box Raw, Box Body);

/// <summary>
/// Sizes a skinned body mesh as it stands in game. Body meshes are stored in the skeleton's bind
/// pose with the arms spread sideways, so their bounds are far wider than the body. The width and
/// depth are taken from the vertices in the waist band, below the spread arms, and widened for the
/// arms hanging beside the torso; the height stays the mesh's full height.
/// </summary>
internal static class WaistBandSizing
{
    /// <summary>The waist band spans these fractions of the mesh height above its lowest point.</summary>
    private const float WaistBandBottom = 0.40f;
    private const float WaistBandTop = 0.55f;

    /// <summary>Width and depth of the torso with the arms hanging beside it, relative to the waist.</summary>
    private const float ArmsBesideTorsoFactor = 1.6f;

    /// <summary>Null when there are no vertices or the mesh is flat. A mesh without vertices in the band keeps its full width and depth.</summary>
    public static SkinnedBodySize? Measure(IReadOnlyList<Vector3[]> meshVertices)
    {
        if (BoundsOf(meshVertices, float.NegativeInfinity, float.PositiveInfinity) is not { } raw || !(raw.Size.Z > 0)) return null;

        var bandBottom = raw.Min.Z + WaistBandBottom * raw.Size.Z;
        var bandTop = raw.Min.Z + WaistBandTop * raw.Size.Z;
        var waist = BoundsOf(meshVertices, bandBottom, bandTop) ?? raw;
        var halfWidth = waist.Size * (0.5f * ArmsBesideTorsoFactor);
        var center = waist.Center;
        var body = new Box(
            new Vector3(center.X - halfWidth.X, center.Y - halfWidth.Y, raw.Min.Z),
            new Vector3(center.X + halfWidth.X, center.Y + halfWidth.Y, raw.Max.Z));
        return new SkinnedBodySize(raw, body);
    }

    /// <summary>Bounds of the vertices whose height lies in [<paramref name="bottom"/>, <paramref name="top"/>]; null when none does.</summary>
    private static Box? BoundsOf(IReadOnlyList<Vector3[]> meshVertices, float bottom, float top)
    {
        Box? bounds = null;
        foreach (var vertices in meshVertices)
        {
            foreach (var vertex in vertices)
            {
                if (!(vertex.Z >= bottom && vertex.Z <= top)) continue;
                var point = new Box(vertex, vertex);
                bounds = bounds is { } found ? found.Union(point) : point;
            }
        }
        return bounds;
    }
}
