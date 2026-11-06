using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>Whether the surfaces of placed objects come within the touch gap of each other; an object whose mesh cannot be read touches nothing.</summary>
/// <param name="inAnotherShape">
/// The objects leaving with no place that stand after in another shape, as they stand then, by id; they leave as moved objects do.
/// </param>
internal sealed class PlacedMeshes(ITriangleMeshes triangles, OtherModsObjectRules rules, float touchGap, IReadOnlyDictionary<OtherId, OtherObject> inAnotherShape)
{
    public bool Touch(OtherObject other, PlacedTransform otherTransform, TargetObject target, TouchScratch scratch) =>
        Touch(rules.MeshPathOf(other), otherTransform, rules.MeshPathOf(target), target.Transform, scratch);

    /// <summary>The leaving object stands somewhere after it left: moved, or in another shape.</summary>
    public bool StandsAfter(OtherLeaving leaving) => leaving.To is not null || inAnotherShape.ContainsKey(leaving.Object.Id);

    /// <summary>The target object touches the leaving object where that one stood and, when it stands after, no longer touches it there.</summary>
    public bool Loses(OtherLeaving leaving, TargetObject target, TouchScratch scratch) =>
        Loses(leaving, rules.MeshPathOf(target), target.Transform, scratch);

    /// <summary>The object touches the leaving object where that one stood and, when it stands after, no longer touches it there.</summary>
    public bool Loses(OtherLeaving leaving, OtherObject other, TouchScratch scratch) =>
        Loses(leaving, rules.MeshPathOf(other), other.Transform, scratch);

    /// <summary>The leaving object stands after and touches the object there.</summary>
    public bool StillTouches(OtherLeaving leaving, OtherObject other, TouchScratch scratch) =>
        StillTouches(leaving, rules.MeshPathOf(other), other.Transform, scratch);

    private bool Loses(OtherLeaving leaving, string mesh, PlacedTransform at, TouchScratch scratch) =>
        Touch(rules.MeshPathOf(leaving.Object), leaving.Object.Transform, mesh, at, scratch) && !StillTouches(leaving, mesh, at, scratch);

    private bool StillTouches(OtherLeaving leaving, string mesh, PlacedTransform at, TouchScratch scratch)
    {
        if (leaving.To is { } to) return Touch(rules.MeshPathOf(leaving.Object), to, mesh, at, scratch);
        return inAnotherShape.TryGetValue(leaving.Object.Id, out var after) && Touch(rules.MeshPathOf(after), after.Transform, mesh, at, scratch);
    }

    private bool Touch(string firstMesh, PlacedTransform first, string secondMesh, PlacedTransform second, TouchScratch scratch)
    {
        using var firstTree = triangles.Acquire(firstMesh);
        using var secondTree = triangles.Acquire(secondMesh);
        return firstTree.Value is { } firstValue
            && secondTree.Value is { } secondValue
            && MeshContact.SurfacesTouch(firstValue, first, secondValue, second, touchGap, scratch);
    }
}
