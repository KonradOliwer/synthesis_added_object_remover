using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Points spread evenly over a mesh's surface, in mesh-local space: about
/// <see cref="TargetSamplesPerMesh"/> in total, each triangle getting a share proportional to its
/// area (so finely tessellated parts do not outweigh large flat ones), at most
/// <see cref="MaxSamplesPerTriangle"/>. Fractional shares are carried over to the next triangle,
/// and the points inside a triangle follow a fixed low-discrepancy pattern, so the result is the
/// same on every run.
/// </summary>
internal static class SurfaceSampler
{
    private const int TargetSamplesPerMesh = 4096;
    private const int MaxSamplesPerTriangle = 16;

    /// <summary>Additive recurrence of the plastic number (R2 sequence), well spread in the unit square.</summary>
    private const float SequenceStepU = 0.7548776662f;
    private const float SequenceStepV = 0.5698402910f;
    private const float SequenceStart = 0.5f;

    /// <summary>Empty when the mesh has no surface area (only points or zero-area triangles).</summary>
    public static Vector3[] Sample(MeshTriangleTree tree)
    {
        var totalArea = 0f;
        for (var t = 0; t < tree.TriangleCount; t++) totalArea += Area(tree.GetTriangle(t));
        if (!(totalArea > 0)) return [];

        var samplesPerArea = TargetSamplesPerMesh / totalArea;
        var samples = new List<Vector3>(TargetSamplesPerMesh);
        var carry = 0f;
        for (var t = 0; t < tree.TriangleCount; t++)
        {
            var triangle = tree.GetTriangle(t);
            carry += Area(triangle) * samplesPerArea;
            var count = (int)carry;
            carry -= count;
            for (var k = 0; k < Math.Min(count, MaxSamplesPerTriangle); k++) samples.Add(PointInTriangle(triangle, k));
        }
        return samples.ToArray();
    }

    private static float Area(MeshTriangle triangle) =>
        0.5f * Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).Length();

    /// <summary>The k-th point of the pattern, mapped uniformly onto the triangle.</summary>
    private static Vector3 PointInTriangle(MeshTriangle triangle, int k)
    {
        var u = Fraction(SequenceStart + k * SequenceStepU);
        var v = Fraction(SequenceStart + k * SequenceStepV);
        var root = MathF.Sqrt(u);
        return triangle.A * (1 - root) + triangle.B * (root * (1 - v)) + triangle.C * (root * v);
    }

    private static float Fraction(float value) => value - MathF.Floor(value);
}
