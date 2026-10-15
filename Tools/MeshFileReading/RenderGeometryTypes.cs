using NiflySharp;
using NiflySharp.Blocks;

namespace AddedObjectRemover;

/// <summary>Decides, once per shape class, whether a shape carries real triangle geometry.</summary>
internal sealed class RenderGeometryTypes
{
    private readonly ComputedOncePerKey<Type, bool> _byType = new(Publication.FirstWriteWins, EqualityComparer<Type>.Default);

    public int ShapeClassesDecided => _byType.Contents().Count;

    public bool IsRenderGeometry(INiShape shape) => _byType.Get(shape.GetType(), () => IsRenderGeometryType(shape.GetType()));

    /// <summary>
    /// Real triangle geometry only: BSTriShape and subclasses, and legacy NiTriShape/NiTriStrips
    /// (NiTriBasedGeom). The particle families (NiParticles, NiPSParticleSystem, ...) have no
    /// usable vertices and share no common base class, so they are recognised by class name.
    /// </summary>
    private static bool IsRenderGeometryType(Type shapeType)
    {
        for (var type = shapeType; type != null && type != typeof(object); type = type.BaseType)
        {
            if (type.Name.Contains("Particle", StringComparison.Ordinal)) return false;
        }
        if (typeof(BSTriShape).IsAssignableFrom(shapeType) || typeof(NiTriShape).IsAssignableFrom(shapeType)) return true;
        if (!typeof(NiGeometry).IsAssignableFrom(shapeType)) return true; // Other non-legacy shape families (e.g. BSGeometry).
        return typeof(NiTriBasedGeom).IsAssignableFrom(shapeType) || typeof(NiTriStrips).IsAssignableFrom(shapeType);
    }
}
