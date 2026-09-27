using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Exactly <see cref="SamplesPerMesh"/> points spread over a mesh's surface in mesh-local space,
/// each standing for the same area. The triangles are laid end to end by area and the points are
/// taken at equal steps along that total (systematic sampling), so every triangle gets a number of
/// points proportional to its area, within one: large flat faces get their full share however
/// finely the rest is tessellated, and a triangle smaller than one step gets at most one point,
/// depending on where the steps fall. Vertices are not added, since a point standing for no area
/// would bias the weights. The points inside a triangle follow a fixed low-discrepancy
/// pattern, so the result is the same on every run.
/// </summary>
internal static class SurfaceSampler
{
    private const int SamplesPerMesh = 4096;

    /// <summary>Where each point lies inside its step along the total area.</summary>
    private const double StepOffset = 0.5;

    /// <summary>Additive recurrence of the plastic number (R2 sequence), well spread in the unit square.</summary>
    private const double SequenceStepU = 0.7548776662466927;
    private const double SequenceStepV = 0.5698402909980532;
    private const double SequenceStart = 0.5;

    /// <summary>Empty when the mesh has no surface area (only points or zero-area triangles).</summary>
    public static Vector3[] Sample(MeshTriangleTree tree)
    {
        var areas = new double[tree.TriangleCount];
        var totalArea = 0.0;
        for (var t = 0; t < areas.Length; t++)
        {
            areas[t] = Area(tree.GetTriangle(t));
            totalArea += areas[t];
        }
        if (!(totalArea > 0)) return [];

        var step = totalArea / SamplesPerMesh;
        var samples = new Vector3[SamplesPerMesh];
        var triangle = 0;
        var areaBeforeTriangle = 0.0;
        var pointsInTriangle = 0;
        for (var i = 0; i < SamplesPerMesh; i++)
        {
            var position = (i + StepOffset) * step;
            while (triangle < areas.Length - 1 && areaBeforeTriangle + areas[triangle] <= position)
            {
                areaBeforeTriangle += areas[triangle];
                triangle++;
                pointsInTriangle = 0;
            }
            samples[i] = PointInTriangle(tree.GetTriangle(triangle), pointsInTriangle++);
        }
        return samples;
    }

    /// <summary>Zero for a triangle with non-finite corners, so it gets no points instead of spoiling the total.</summary>
    private static double Area(MeshTriangle triangle)
    {
        var area = 0.5 * Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).Length();
        return double.IsFinite(area) ? area : 0;
    }

    /// <summary>The k-th point of the pattern, mapped uniformly onto the triangle.</summary>
    private static Vector3 PointInTriangle(MeshTriangle triangle, int k)
    {
        var u = Fraction(SequenceStart + k * SequenceStepU);
        var v = Fraction(SequenceStart + k * SequenceStepV);
        var root = MathF.Sqrt(u);
        return triangle.A * (1 - root) + triangle.B * (root * (1 - v)) + triangle.C * (root * v);
    }

    private static float Fraction(double value) => (float)(value - Math.Floor(value));
}
