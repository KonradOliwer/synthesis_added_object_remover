using System.Numerics;
using System.Reflection;
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Extensions;
using NiflySharp.Helpers;
using NiflySharp.Structs;

namespace AddedObjectRemover;

internal enum StripFault
{
    None,

    /// <summary>The strip lengths do not add up to the strip points.</summary>
    LengthsMismatch,

    /// <summary>This NiflySharp version lacks the strip fields.</summary>
    FieldsMissing,
}

/// <summary>
/// Per-shape access to NiflySharp shape blocks: type filtering, vertices and triangles. Library
/// failures on malformed data surface as <see cref="MalformedNifException"/>.
/// </summary>
internal static class NifShapes
{
    private const BindingFlags StripFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo? StripPointsField = typeof(NiTriStripsData).GetField("_points", StripFieldFlags);
    private static readonly FieldInfo? StripLengthsField = typeof(NiTriStripsData).GetField("_stripLengths", StripFieldFlags);
    public static readonly ArchiveProblem StripFieldsMissing = new(
        ArchiveProblemKind.StripFieldsMissing,
        "NiTriStripsData",
        "Warning: NiTriStripsData._points/_stripLengths not found in this NiflySharp version; "
        + "NiTriStrips shapes are used as points in the touch test.");

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
    /// <param name="stripFault">Why a NiTriStrips shape gave no triangles, when it could not; the result is then null.</param>
    public static List<Triangle>? GetTriangles(INiShape shape, out StripFault stripFault)
    {
        stripFault = StripFault.None;
        if (shape is BSGeometry) return null;
        if (NiflyCalls.Call(() => shape.Triangles) is { Count: > 0 } triangles) return triangles;
        return NiflyCalls.Call(() => shape.GeometryData) is NiTriStripsData data
            ? GetStripTriangles(data, out stripFault)
            : null;
    }

    /// <summary>
    /// NiTriStripsData has no public strip API (NiflySharp's generator skips it), so the protected
    /// <c>_points</c> (flat index list, sum of <c>_stripLengths</c> entries) and <c>_stripLengths</c>
    /// are read by reflection.
    /// </summary>
    private static List<Triangle>? GetStripTriangles(NiTriStripsData data, out StripFault stripFault)
    {
        stripFault = StripFault.None;
        if (StripPointsField == null || StripLengthsField == null)
        {
            stripFault = StripFault.FieldsMissing;
            return null;
        }
        if (StripPointsField.GetValue(data) is not List<ushort> { Count: > 0 } points
            || StripLengthsField.GetValue(data) is not List<ushort> { Count: > 0 } stripLengths)
            return null;

        // SplitByFlexSize silently truncates when the lengths do not add up to the point count.
        if (stripLengths.Sum(length => (int)length) != points.Count)
        {
            stripFault = StripFault.LengthsMismatch;
            return null;
        }
        var strips = points.SplitByFlexSize(stripLengths).ToList();
        return IndicesHelper.GenerateTrianglesFromStrips(strips);
    }

    /// <summary>
    /// Whether the shape's shader property block is a BSEffectShaderProperty: effect surfaces (fog,
    /// light rays, water spray, mist planes) that are drawn but are nothing solid.
    /// </summary>
    private static bool HasEffectShader(INiShape shape, List<INiObject> blocks)
    {
        var shaderIndex = NiflyCalls.Call(() => shape.HasShaderProperty ? shape.ShaderPropertyRef.Index : -1);
        return shaderIndex >= 0 && shaderIndex < blocks.Count && blocks[shaderIndex] is BSEffectShaderProperty;
    }

    public static MeshShapeKind KindOf(INiShape shape, List<INiObject> blocks, ShapeInclusion inclusion)
    {
        if (inclusion.IsEditorMarkerName(shape.Name?.String)) return MeshShapeKind.EditorMarker;
        return HasEffectShader(shape, blocks) ? MeshShapeKind.EffectShader : MeshShapeKind.Solid;
    }

    public static MeshShapeKind KindOf(NiNode node, ShapeInclusion inclusion) =>
        inclusion.IsEditorMarkerName(node.Name?.String) ? MeshShapeKind.EditorMarker : MeshShapeKind.Solid;
}
