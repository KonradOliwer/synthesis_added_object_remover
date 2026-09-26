using System.Diagnostics;

namespace AddedObjectRemover;

/// <summary>Mesh path of each target object's base; none where its bounds did not come from a readable mesh.</summary>
internal sealed class TargetMeshPaths(IReadOnlyList<TargetObject> targets, BaseObjectShapeProvider shapes)
{
    private readonly string?[] _paths = targets.Select(target => shapes.GetMeshPath(target.Base)).ToArray();

    public bool HasMesh(int target) => _paths[target] != null;

    /// <remarks>Only targets with a mesh take part in the touch search and in anchoring.</remarks>
    public string Get(int target) =>
        _paths[target] ?? throw new UnreachableException($"Target {target} has no mesh but was used as a mesh.");
}
