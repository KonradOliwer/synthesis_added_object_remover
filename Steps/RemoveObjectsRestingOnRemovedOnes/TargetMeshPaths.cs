using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>Mesh path of each target object's base; none where its bounds did not come from a readable mesh.</summary>
internal sealed class TargetMeshPaths
{
    private readonly PerIndexTable<string> _paths;
    private readonly int _targetCount;

    public TargetMeshPaths(IReadOnlyList<TargetObject> targets, IBaseObjectShapes shapes)
        : this(targets.Select(target => shapes.Of(target.Base).MeshPath).ToArray())
    {
    }

    /// <param name="paths">Per target index; null where the target has no mesh.</param>
    public TargetMeshPaths(string?[] paths)
    {
        _targetCount = paths.Length;
        _paths = PerIndexTable<string>.From(
            paths.Select((path, target) => (Target: target, Path: path)).Where(entry => entry.Path != null),
            entry => entry.Target,
            entry => entry.Path!);
    }

    /// <exception cref="ArgumentOutOfRangeException">The index is not a target's.</exception>
    public bool HasMesh(int target)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)target, (uint)_targetCount);
        return _paths.Has(target);
    }

    public string Get(int target) =>
        _paths.TryGet(target, out var path)
            ? path
            : throw new UnreachableException($"Target {target} has no mesh but was used as a mesh.");
}
