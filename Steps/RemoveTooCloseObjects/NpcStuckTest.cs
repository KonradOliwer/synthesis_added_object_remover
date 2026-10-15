using System.Numerics;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// Whether an NPC's body is stuck in an object at the object's real size: the body must penetrate
/// the object by more than <see cref="FootClearance"/>, so an NPC standing on the object or
/// brushing against it is not stuck. The body box shrunk by the clearance on every side (its
/// core) is stuck when an object triangle overlaps it, or when the core lies inside the object,
/// i.e. the object's mesh surrounds it (<see cref="MeshContact.PointInside"/>), open meshes included:
/// an NPC standing in a room of the object counts as inside it.
/// </summary>
internal static class NpcStuckTest
{
    /// <summary>World units an NPC may sink into or lean against an object without being stuck.</summary>
    public const float FootClearance = 8f;

    /// <summary>
    /// Stuck when any of the bodies is. Several bodies are first tested for triangle overlap
    /// together, as the union of their cores, which holds every body's core: when no triangle
    /// overlaps it, none overlaps any core, and only each core's inside test is left.
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

        scratch.CoreTests++;
        if (!OverlapsAnyTriangle(objectTree, objectTransform, Box.UnionAll(cores), bodyTransform, scratch))
        {
            return cores.Any(core => IsInside(objectTree, objectTransform, core, bodyTransform, scratch));
        }
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
        var clearance = FootClearance / bodyScale;
        var acrossCore = body.Inset(clearance);
        var soles = body.Min.Z + clearance;
        return new Box(
            new Vector3(acrossCore.Min.X, acrossCore.Min.Y, soles),
            new Vector3(acrossCore.Max.X, acrossCore.Max.Y, MathF.Max(body.Max.Z - clearance, soles)));
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
            || IsInside(objectTree, objectTransform, core, bodyTransform, scratch);
    }

    private static bool OverlapsAnyTriangle(
        MeshTriangleTree objectTree,
        PlacedTransform objectTransform,
        Box core,
        PlacedTransform bodyTransform,
        NpcScratch scratch) =>
        MeshContact.BoxOverlapsMesh(objectTree, objectTransform, core, bodyTransform, scratch.Triangles);

    /// <summary>Called only when no triangle overlaps the core, so the core is wholly inside or wholly outside and its centre decides.</summary>
    private static bool IsInside(
        MeshTriangleTree objectTree,
        PlacedTransform objectTransform,
        Box core,
        PlacedTransform bodyTransform,
        NpcScratch scratch)
    {
        var center = objectTransform.ToLocal(bodyTransform.ToWorld(core.Center));
        return MeshContact.PointInside(objectTree, center, objectTransform.Rotation, scratch.Triangles);
    }
}
