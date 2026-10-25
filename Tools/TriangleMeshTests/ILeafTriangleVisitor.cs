namespace AddedObjectRemover;

/// <summary>Is handed the triangles of a <see cref="MeshTriangleTree"/> walk one by one; accepting one ends the walk.</summary>
public interface ILeafTriangleVisitor
{
    bool Accepts(int triangle);
}
