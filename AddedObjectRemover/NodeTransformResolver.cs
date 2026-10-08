using NiflySharp;
using NiflySharp.Blocks;

namespace AddedObjectRemover;

internal enum NodeSkip { None, Hidden, Marker, Unreachable }

/// <summary>A node's cumulative node-space -> root-space transform, or why its subtree is skipped.</summary>
internal readonly record struct NodeState(Similarity ToRoot, NodeSkip Skip);

/// <summary>
/// Resolves (and memoizes) NiNode states by walking parent links up to the root node. A node is
/// skipped when it is unreachable from the root (or in a cyclic/implausibly deep chain), hidden,
/// of a kind the inclusion skips, or has such an ancestor.
/// </summary>
internal sealed class NodeTransformResolver
{
    /// <summary>Cycle / malformed-graph guard for the parent walk.</summary>
    private const int MaxDepth = 256;

    private static readonly NodeState UnreachableState = new(Similarity.Identity, NodeSkip.Unreachable);

    private readonly List<INiObject> _blocks;
    private readonly IReadOnlyDictionary<int, int> _parentOf;
    private readonly AvObjectFlags _flags;
    private readonly ShapeInclusion _inclusion;
    private readonly Dictionary<int, NodeState> _states;

    public NodeTransformResolver(
        List<INiObject> blocks,
        IReadOnlyDictionary<int, int> parentOf,
        int rootIndex,
        AvObjectFlags flags,
        ShapeInclusion inclusion)
    {
        _blocks = blocks;
        _parentOf = parentOf;
        _flags = flags;
        _inclusion = inclusion;
        _states = new Dictionary<int, NodeState> { [rootIndex] = RootState(blocks[rootIndex]) };
    }

    public NodeState Resolve(int nodeIndex)
    {
        var unresolved = CollectUnresolvedAncestors(nodeIndex, out var state);

        // Fold from the top-most unresolved ancestor down to the requested node.
        for (var i = unresolved.Count - 1; i >= 0; i--)
        {
            if (state.Skip == NodeSkip.None) state = ChildState(state, _blocks[unresolved[i]]);
            _states[unresolved[i]] = state;
        }
        return state;
    }

    /// <summary>
    /// The node and its ancestors up to (excluding) the first already resolved one, whose state is
    /// returned in <paramref name="resolvedAncestor"/> (unreachable when the chain breaks).
    /// </summary>
    private List<int> CollectUnresolvedAncestors(int nodeIndex, out NodeState resolvedAncestor)
    {
        var path = new List<int>();
        var onPath = new HashSet<int>();
        var cursor = nodeIndex;
        while (!_states.TryGetValue(cursor, out resolvedAncestor))
        {
            if (path.Count >= MaxDepth || !onPath.Add(cursor))
            {
                resolvedAncestor = UnreachableState;
                break;
            }
            path.Add(cursor);
            if (!_parentOf.TryGetValue(cursor, out cursor))
            {
                resolvedAncestor = UnreachableState;
                break;
            }
        }
        return path;
    }

    /// <summary>
    /// The root's own transform is left out (the engine replaces it with the placed reference's),
    /// but its hidden flag and marker name count like any other node's.
    /// </summary>
    private NodeState RootState(INiObject block) =>
        block is NiNode node ? new NodeState(Similarity.Identity, SkipOf(node)) : UnreachableState;

    private NodeState ChildState(NodeState parent, INiObject block)
    {
        if (block is not NiNode node) return UnreachableState;
        var skip = SkipOf(node);
        return skip == NodeSkip.None
            ? new NodeState(parent.ToRoot.After(Similarity.From(node.Translation, node.Rotation, node.Scale)), NodeSkip.None)
            : new NodeState(Similarity.Identity, skip);
    }

    private NodeSkip SkipOf(NiNode node)
    {
        if (_inclusion.SkippedAncestorKinds.Contains(NifShapes.KindOf(node, _inclusion))) return NodeSkip.Marker;
        if (!_inclusion.IncludeHidden && _flags.IsHiddenWithoutController(node.Flags_ui, node.Flags_us, node.Controller)) return NodeSkip.Hidden;
        return NodeSkip.None;
    }
}
