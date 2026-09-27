using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// The ObjectShape removal zone of one placed object: its own mesh, enlarged uniformly by
/// (1 + 2 × multiplier) around the centre of its local box, as a transform for the mesh's existing
/// triangles (the bubble), plus the box enclosing that enlarged mesh.
/// </summary>
/// <param name="Box">The object's box enlarged the same way; the same zone as the BoundingBox mode.</param>
/// <param name="Bubble">Places the object's mesh-local triangles as the enlarged mesh in the world.</param>
internal readonly record struct ShapeZone(OrientedBox Box, PlacedTransform Bubble)
{
    /// <summary>The zone grows by the multiplier times the object's size on both sides of each axis.</summary>
    private const float SidesPerAxis = 2f;

    public static ShapeZone Create(Box localBox, PlacedTransform transform, float multiplier)
    {
        var factor = 1f + SidesPerAxis * multiplier;
        var zoneBox = OrientedBox.FromLocal(Geometry.ExpandedLocalBox(localBox, 1f, multiplier), transform);
        return new ShapeZone(zoneBox, EnlargeAroundCentre(transform, localBox.Center, factor));
    }

    /// <summary>
    /// world = position + R × (scale × (centre + factor × (x − centre))), rewritten as a placement:
    /// position + R × (scale × (1 − factor) × centre) + R × (scale × factor × x).
    /// </summary>
    private static PlacedTransform EnlargeAroundCentre(PlacedTransform transform, Vector3 centre, float factor) => new(
        transform.Position + transform.Rotation.Transform(centre * (transform.Scale * (1f - factor))),
        transform.Rotation,
        transform.Scale * factor);
}
