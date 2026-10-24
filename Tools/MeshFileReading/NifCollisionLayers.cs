using System.Collections.Immutable;
using NiflySharp;
using NiflySharp.Blocks;

namespace AddedObjectRemover;

/// <summary>
/// The layers of a NIF's collision rigid bodies. A collision object counts when the object it is attached to is reachable
/// from the root node through nodes the inclusion does not skip by kind; hidden nodes count, as the hidden flag only stops
/// rendering. Phantom collision objects (trigger volumes) hold no rigid body and never count.
/// </summary>
internal static class NifCollisionLayers
{
    public static ImmutableArray<CollisionLayer> Collect(
        List<INiObject> blocks, IReadOnlyDictionary<int, int> parentOf, int rootIndex, AvObjectFlags flags, ShapeInclusion inclusion)
    {
        var nodes = new NodeTransformResolver(blocks, parentOf, rootIndex, flags, inclusion.WithHiddenIncluded());
        var layers = new SortedSet<CollisionLayer>();
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            if (blocks[blockIndex] is NiAVObject owner && FindRigidBody(owner, blocks) is { } body && IsReachable(blockIndex, owner, parentOf, nodes))
            {
                layers.Add((CollisionLayer)body.HavokFilter.Layer_SL);
            }
        }
        return [.. layers];
    }

    private static bhkRigidBody? FindRigidBody(NiAVObject owner, List<INiObject> blocks) =>
        BlockAt(owner.CollisionObject?.Index, blocks) is bhkNiCollisionObject collision
            ? BlockAt(collision.Body?.Index, blocks) as bhkRigidBody
            : null;

    private static INiObject? BlockAt(int? index, List<INiObject> blocks) =>
        index is { } at && at >= 0 && at < blocks.Count ? blocks[at] : null;

    /// <summary>A node is looked up itself; a shape through the node holding it.</summary>
    private static bool IsReachable(int blockIndex, NiAVObject owner, IReadOnlyDictionary<int, int> parentOf, NodeTransformResolver nodes)
    {
        if (owner is NiNode) return nodes.Resolve(blockIndex).Skip == NodeSkip.None;
        return parentOf.TryGetValue(blockIndex, out var parentIndex) && nodes.Resolve(parentIndex).Skip == NodeSkip.None;
    }
}
