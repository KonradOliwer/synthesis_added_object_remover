using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether an NPC's body is stuck in an object at the object's real size: the body must penetrate
/// the object by more than <see cref="FootClearance"/>, so an NPC standing on the object or
/// brushing against it is not stuck. The body box shrunk by the clearance on every side (its
/// core) is stuck when an object triangle overlaps it, or, for a closed (solid) object only, when
/// the core lies inside the object (<see cref="SurroundingRayTest"/>); an NPC walking inside a
/// hollow object is not stuck.
/// </summary>
internal static class NpcStuckTest
{
    /// <summary>World units an NPC may sink into or lean against an object without being stuck.</summary>
    public const float FootClearance = 8f;

    /// <summary>
    /// Stuck when any of the bodies is. Several bodies are first tested together as the union of
    /// their cores, which holds every body's core, so a miss there clears them all.
    /// </summary>
    public static bool IsAnyBodyStuck(
        MeshTriangleTree objectTree,
        PlacedTransform objectTransform,
        NpcBodySet bodies,
        PlacedTransform bodyTransform,
        NpcScratch scratch)
    {
        var cores = scratch.Cores;
        cores.Clear();
        foreach (var body in bodies.Bodies) cores.Add(GetCore(body.LocalBox, bodyTransform.Scale));
        if (cores.Count == 1) return IsCoreStuck(objectTree, objectTransform, cores[0], bodyTransform, scratch);

        var combined = cores[0];
        foreach (var core in cores) combined = combined.Union(core);
        if (!IsCoreStuck(objectTree, objectTransform, combined, bodyTransform, scratch)) return false;
        foreach (var core in cores)
        {
            if (IsCoreStuck(objectTree, objectTransform, core, bodyTransform, scratch)) return true;
        }
        return false;
    }

    /// <summary>
    /// The body box shrunk by <see cref="FootClearance"/> (in world units) on every side. Across,
    /// a body thinner than twice the clearance keeps its centre line. Upwards the feet must always
    /// sink deeper than the clearance, so a body lower than twice the clearance (a point body
    /// included) keeps only the level <see cref="FootClearance"/> above its soles.
    /// </summary>
    /// <param name="bodyScale">The placed reference's scale.</param>
    public static Box GetCore(Box body, float bodyScale)
    {
        var inset = new Vector3(FootClearance / bodyScale);
        var shrunkMin = body.Min + inset;
        var shrunkMax = body.Max - inset;
        var center = body.Center;
        return new Box(
            new Vector3(MathF.Min(shrunkMin.X, center.X), MathF.Min(shrunkMin.Y, center.Y), shrunkMin.Z),
            new Vector3(MathF.Max(shrunkMax.X, center.X), MathF.Max(shrunkMax.Y, center.Y), MathF.Max(shrunkMax.Z, shrunkMin.Z)));
    }

    private static bool IsCoreStuck(
        MeshTriangleTree objectTree,
        PlacedTransform objectTransform,
        Box core,
        PlacedTransform bodyTransform,
        NpcScratch scratch)
    {
        scratch.CoreTests++;
        return OverlapsAnyTriangle(objectTree, objectTransform, core, bodyTransform, scratch)
            || objectTree.IsClosed && IsInside(objectTree, objectTransform, core, bodyTransform, scratch);
    }

    private static bool OverlapsAnyTriangle(
        MeshTriangleTree objectTree,
        PlacedTransform objectTransform,
        Box core,
        PlacedTransform bodyTransform,
        NpcScratch scratch)
    {
        var coreInObject = RelativeTransform.Create(from: bodyTransform, to: objectTransform).ApplyToBox(core);
        if (!coreInObject.Overlaps(objectTree.Bounds)) return false;

        var toBody = RelativeTransform.Create(from: objectTransform, to: bodyTransform);
        objectTree.CollectLeafTriangles(coreInObject, scratch.Triangles);
        foreach (var triangle in scratch.Triangles)
        {
            if (TriangleBoxOverlap.Overlaps(toBody.Apply(objectTree.GetTriangle(triangle)), core)) return true;
        }
        return false;
    }

    /// <summary>Called only when no triangle overlaps the core, so the core is wholly inside or wholly outside and its centre decides.</summary>
    private static bool IsInside(
        MeshTriangleTree objectTree,
        PlacedTransform objectTransform,
        Box core,
        PlacedTransform bodyTransform,
        NpcScratch scratch)
    {
        var center = objectTransform.ToLocal(bodyTransform.ToWorld(core.Center));
        return SurroundingRayTest.IsSurrounded(objectTree, center, objectTransform.Rotation, scratch.Triangles);
    }
}
