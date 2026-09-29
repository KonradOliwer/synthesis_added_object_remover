using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Extensions;
using NiflySharp.Helpers;
using NiflySharp.Structs;

namespace AddedObjectRemover;

/// <summary>
/// Per-shape access to NiflySharp shape blocks: type filtering, vertices and triangles. Library
/// failures on malformed data surface as <see cref="MalformedNifException"/>.
/// </summary>
internal static class NifShapes
{
    private const BindingFlags StripFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo? StripPointsField = typeof(NiTriStripsData).GetField("_points", StripFieldFlags);
    private static readonly FieldInfo? StripLengthsField = typeof(NiTriStripsData).GetField("_stripLengths", StripFieldFlags);
    private static readonly ArchiveProblem StripFieldsMissing = new(
        ArchiveProblemKind.StripFieldsMissing,
        "NiTriStripsData",
        "Warning: NiTriStripsData._points/_stripLengths not found in this NiflySharp version; "
        + "NiTriStrips shapes are used as points in the touch test.");

    private static volatile ArchiveProblem? _stripFieldsProblem;

    /// <summary>Set once a strips shape needed the strip fields this NiflySharp version lacks; null until then.</summary>
    public static ArchiveProblem? StripFieldsProblem => _stripFieldsProblem;

    private static readonly ConcurrentDictionary<Type, bool> RenderGeometryTypes = new();

    public static bool IsRenderGeometry(INiShape shape) =>
        RenderGeometryTypes.GetOrAdd(shape.GetType(), static type => IsRenderGeometryType(type));

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

    /// <summary>
    /// Vertex positions in shape space, or null when the shape carries none (the bounding sphere
    /// is used instead).
    /// <list type="bullet">
    /// <item>BSDynamicTriShape: positions live in its dynamic vertex list (<c>Vertices</c>,
    /// Vector4 xyz). Its vertex descriptor never has the Vertex flag, and NifFile.PrepareData only
    /// copies the dynamic data into the regular vertex data for skinned shapes, so the regular
    /// vertex data of a non-skinned dynamic shape is all zeros.</item>
    /// <item>BSTriShape: regular vertex data, populated from NiSkinPartition for skinned SSE
    /// shapes by NifFile.PrepareData.</item>
    /// <item>Legacy NiTriShape/NiTriStrips: the NiGeometryData block.</item>
    /// </list>
    /// </summary>
    public static List<Vector3>? GetVerticesOrNull(INiShape shape) => shape switch
    {
        BSDynamicTriShape dynamicShape => ToPositions(NiflyCalls.Call(() => dynamicShape.Vertices)),
        BSTriShape triShape => NiflyCalls.Call(() =>
            triShape.VertexCount > 0 && (triShape.HasVertices || triShape.IsSkinned) ? triShape.VertexPositions : null),
        _ => NiflyCalls.Call(() => shape.GeometryData?.Vertices),
    };

    private static List<Vector3>? ToPositions(List<Vector4>? dynamicVertices)
    {
        if (dynamicVertices is not { Count: > 0 }) return null;
        var positions = new List<Vector3>(dynamicVertices.Count);
        foreach (var v in dynamicVertices) positions.Add(new Vector3(v.X, v.Y, v.Z));
        return positions;
    }

    /// <summary>
    /// The shape's own triangle list (BSTriShape family, NiTriShape), or for legacy NiTriStrips the
    /// strips of its NiTriStripsData converted to triangles (alternating winding, degenerate
    /// triangles and strips shorter than 3 points skipped). BSGeometry (Starfield) has no triangle
    /// access in NiflySharp.
    /// </summary>
    /// <param name="stripsMismatched">True when the strip lengths do not match the strip points; the result is then null.</param>
    public static List<Triangle>? GetTriangles(INiShape shape, out bool stripsMismatched)
    {
        stripsMismatched = false;
        if (shape is BSGeometry) return null;
        if (NiflyCalls.Call(() => shape.Triangles) is { Count: > 0 } triangles) return triangles;
        return NiflyCalls.Call(() => shape.GeometryData) is NiTriStripsData data
            ? GetStripTriangles(data, out stripsMismatched)
            : null;
    }

    /// <summary>
    /// NiTriStripsData has no public strip API (NiflySharp's generator skips it), so the protected
    /// <c>_points</c> (flat index list, sum of <c>_stripLengths</c> entries) and <c>_stripLengths</c>
    /// are read by reflection.
    /// </summary>
    private static List<Triangle>? GetStripTriangles(NiTriStripsData data, out bool stripsMismatched)
    {
        stripsMismatched = false;
        if (StripPointsField == null || StripLengthsField == null)
        {
            _stripFieldsProblem = StripFieldsMissing;
            return null;
        }
        if (StripPointsField.GetValue(data) is not List<ushort> { Count: > 0 } points
            || StripLengthsField.GetValue(data) is not List<ushort> { Count: > 0 } stripLengths)
            return null;

        // SplitByFlexSize silently truncates when the lengths do not add up to the point count.
        if (stripLengths.Sum(length => (int)length) != points.Count)
        {
            stripsMismatched = true;
            return null;
        }
        var strips = points.SplitByFlexSize(stripLengths).ToList();
        return IndicesHelper.GenerateTrianglesFromStrips(strips);
    }

    /// <summary>
    /// Whether the shape's shader property block is a BSEffectShaderProperty: effect surfaces (fog,
    /// light rays, water spray, mist planes) that are drawn but are nothing solid.
    /// </summary>
    public static bool HasEffectShader(INiShape shape, List<INiObject> blocks)
    {
        var shaderIndex = NiflyCalls.Call(() => shape.HasShaderProperty ? shape.ShaderPropertyRef.Index : -1);
        return shaderIndex >= 0 && shaderIndex < blocks.Count && blocks[shaderIndex] is BSEffectShaderProperty;
    }

    /// <summary>Editor markers have no dedicated API; they are recognised by the Creation Kit naming convention.</summary>
    public static bool IsEditorMarker(string? name) =>
        name != null && name.Contains("EditorMarker", StringComparison.OrdinalIgnoreCase);
}
