using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Axis-aligned box given by its minimum and maximum corners.</summary>
public readonly record struct Box(Vector3 Min, Vector3 Max)
{
    public static Box Zero => new(Vector3.Zero, Vector3.Zero);

    public Vector3 Center => (Min + Max) * 0.5f;

    public Vector3 Size => Max - Min;

    public Vector3 HalfExtents => Size * 0.5f;

    /// <summary>Builds a box from two arbitrary corners (order per axis does not matter).</summary>
    public static Box FromCorners(Vector3 a, Vector3 b) => new(Vector3.Min(a, b), Vector3.Max(a, b));

    /// <summary>Both corners multiplied by <paramref name="scale"/>, re-ordered (a negative scale flips them).</summary>
    public Box Scaled(float scale) => FromCorners(Min * scale, Max * scale);

    /// <summary>Inclusive containment test.</summary>
    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X
        && p.Y >= Min.Y && p.Y <= Max.Y
        && p.Z >= Min.Z && p.Z <= Max.Z;

    /// <summary>Inclusive overlap test (touching faces count).</summary>
    public bool Overlaps(Box other) =>
        Min.X <= other.Max.X && other.Min.X <= Max.X
        && Min.Y <= other.Max.Y && other.Min.Y <= Max.Y
        && Min.Z <= other.Max.Z && other.Min.Z <= Max.Z;

    /// <summary>This box grown by <paramref name="padding"/> on every side.</summary>
    public Box Grown(float padding) => new(Min - new Vector3(padding), Max + new Vector3(padding));

    public Box Union(Box other) => new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    /// <summary>The smallest box holding every box; throws for none.</summary>
    public static Box UnionAll(IEnumerable<Box> boxes) => boxes.Aggregate((union, box) => union.Union(box));

    /// <summary>This box shrunk by <paramref name="by"/> on every side; on an axis where it is thinner than twice <paramref name="by"/> it keeps its centre line.</summary>
    public Box Inset(float by)
    {
        var inset = new Vector3(by);
        var center = Center;
        return new Box(Vector3.Min(Min + inset, center), Vector3.Max(Max - inset, center));
    }
}
