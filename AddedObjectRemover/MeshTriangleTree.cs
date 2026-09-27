using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Bounding volume hierarchy over one mesh's triangles, in mesh-local (NIF root) space. Inner
/// nodes are split at the middle of their triangles' centroid bounds along the longest axis; the
/// build needs memory proportional to the triangle count only. Read-only after construction and
/// safe to query from many threads at once.
/// </summary>
internal sealed class MeshTriangleTree
{
    /// <summary>A mesh with more triangles is not indexed and never touches anything.</summary>
    public const int MaxTriangles = 2_000_000;

    private const int LeafTriangles = 4;
    private const int StackallocDepth = 64;

    private const long BytesPerVector3 = 12;
    private const long BytesPerInt = 4;
    private const long BytesPerNode = 32;

    /// <summary>A leaf lists <see cref="Count"/> triangles from <see cref="First"/> in the order array; an inner node (Count 0) has its children at First and First + 1.</summary>
    private readonly record struct Node(Box Bounds, int First, int Count)
    {
        public bool IsLeaf => Count > 0;
    }

    private readonly record struct PendingNode(int Node, int Start, int End, int Depth);

    private readonly NifGeometry _geometry;
    private readonly Node[] _nodes;
    private readonly int[] _order;
    private readonly int _depth;
    private readonly Lazy<bool> _isClosed;

    private MeshTriangleTree(NifGeometry geometry, Node[] nodes, int[] order, int depth)
    {
        _geometry = geometry;
        _nodes = nodes;
        _order = order;
        _depth = depth;
        _isClosed = new Lazy<bool>(() => MeshClosedness.IsClosed(this), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int TriangleCount => _geometry.TriangleCount;

    /// <summary>Whether the mesh has no open edges (<see cref="MeshClosedness"/>); worked out on first use.</summary>
    public bool IsClosed => _isClosed.Value;

    /// <summary>AABB of all triangles.</summary>
    public Box Bounds => _nodes[0].Bounds;

    /// <summary>Approximate managed memory held by this tree and its triangles, in bytes.</summary>
    public long EstimatedBytes =>
        _geometry.Vertices.Length * BytesPerVector3
        + _geometry.Indices.Length * BytesPerInt
        + _nodes.Length * BytesPerNode
        + _order.Length * BytesPerInt;

    /// <summary>Null when the mesh has no triangles or more than <see cref="MaxTriangles"/>.</summary>
    public static MeshTriangleTree? Build(NifGeometry geometry)
    {
        if (geometry.TriangleCount is 0 or > MaxTriangles) return null;

        var triangleBounds = new Box[geometry.TriangleCount];
        var centroids = new Vector3[geometry.TriangleCount];
        var order = new int[geometry.TriangleCount];
        for (var t = 0; t < geometry.TriangleCount; t++)
        {
            triangleBounds[t] = GetTriangle(geometry, t).Bounds;
            centroids[t] = triangleBounds[t].Center;
            order[t] = t;
        }

        var (nodes, depth) = BuildNodes(triangleBounds, centroids, order);
        return new MeshTriangleTree(geometry, nodes, order, depth);
    }

    public MeshTriangle GetTriangle(int triangle) => GetTriangle(_geometry, triangle);

    /// <summary>Replaces <paramref name="output"/> with the triangles of every leaf overlapping <paramref name="query"/>, in a fixed order.</summary>
    public void CollectLeafTriangles(Box query, List<int> output)
    {
        output.Clear();
        var stackSize = _depth + 1;
        Span<int> stack = stackSize <= StackallocDepth ? stackalloc int[StackallocDepth] : new int[stackSize];
        var top = 0;
        stack[top++] = 0;
        while (top > 0)
        {
            var node = _nodes[stack[--top]];
            if (!node.Bounds.Overlaps(query)) continue;
            if (node.IsLeaf)
            {
                for (var i = node.First; i < node.First + node.Count; i++) output.Add(_order[i]);
                continue;
            }
            stack[top++] = node.First + 1;
            stack[top++] = node.First;
        }
    }

    private static MeshTriangle GetTriangle(NifGeometry geometry, int triangle)
    {
        var (a, b, c) = geometry.GetTriangle(triangle);
        return new MeshTriangle(a, b, c);
    }

    /// <summary>Iterative top-down build (no recursion, so skewed meshes cannot overflow the stack); reorders <paramref name="order"/> into leaf ranges.</summary>
    private static (Node[] Nodes, int Depth) BuildNodes(Box[] triangleBounds, Vector3[] centroids, int[] order)
    {
        var nodes = new List<Node> { default };
        var pending = new Stack<PendingNode>();
        pending.Push(new PendingNode(0, 0, order.Length, 1));
        var depth = 1;
        while (pending.TryPop(out var item))
        {
            depth = Math.Max(depth, item.Depth);
            var bounds = UnionOfBounds(triangleBounds, order, item.Start, item.End);
            if (item.End - item.Start <= LeafTriangles)
            {
                nodes[item.Node] = new Node(bounds, item.Start, item.End - item.Start);
                continue;
            }

            var middle = SplitRange(centroids, order, item.Start, item.End);
            var left = nodes.Count;
            nodes.Add(default);
            nodes.Add(default);
            nodes[item.Node] = new Node(bounds, left, 0);
            pending.Push(new PendingNode(left, item.Start, middle, item.Depth + 1));
            pending.Push(new PendingNode(left + 1, middle, item.End, item.Depth + 1));
        }
        return (nodes.ToArray(), depth);
    }

    private static Box UnionOfBounds(Box[] triangleBounds, int[] order, int start, int end)
    {
        var bounds = triangleBounds[order[start]];
        for (var i = start + 1; i < end; i++) bounds = bounds.Union(triangleBounds[order[i]]);
        return bounds;
    }

    /// <summary>
    /// Partitions the range at the middle of its centroid bounds along their longest axis and
    /// returns the split index. When all centroids fall on one side (e.g. they coincide), the
    /// range is split in half by count instead.
    /// </summary>
    private static int SplitRange(Vector3[] centroids, int[] order, int start, int end)
    {
        var centroidBounds = new Box(centroids[order[start]], centroids[order[start]]);
        for (var i = start + 1; i < end; i++) centroidBounds = centroidBounds.Union(new Box(centroids[order[i]], centroids[order[i]]));

        var axis = LongestAxis(centroidBounds.Size);
        var split = centroidBounds.Center[axis];
        var middle = start;
        for (var i = start; i < end; i++)
        {
            if (centroids[order[i]][axis] >= split) continue;
            (order[i], order[middle]) = (order[middle], order[i]);
            middle++;
        }
        return middle == start || middle == end ? start + (end - start) / 2 : middle;
    }

    private static int LongestAxis(Vector3 size)
    {
        if (size.X >= size.Y && size.X >= size.Z) return 0;
        return size.Y >= size.Z ? 1 : 2;
    }
}
