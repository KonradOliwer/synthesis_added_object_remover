using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Extensions;
using NiflySharp.Helpers;
using NiflySharp.Structs;

namespace AddedObjectRemover;

/// <summary>Per-shape access to NiflySharp shape blocks: type filtering, vertices and triangles.</summary>
internal static class NifShapes
{
    private const BindingFlags StripFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo? StripPointsField = typeof(NiTriStripsData).GetField("_points", StripFieldFlags);
    private static readonly FieldInfo? StripLengthsField = typeof(NiTriStripsData).GetField("_stripLengths", StripFieldFlags);
    private static int _stripFieldsMissingReported;

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
    /// Vertex positions in shape space, or null when the shape carries none (caller then uses
    /// the bounding sphere).
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
    public static List<Vector3>? TryGetVertices(INiShape shape)
    {
        switch (shape)
        {
            case BSDynamicTriShape dynamicShape:
                if (dynamicShape.Vertices is not { Count: > 0 } dynamicVertices) return null;
                var positions = new List<Vector3>(dynamicVertices.Count);
                foreach (var v in dynamicVertices) positions.Add(new Vector3(v.X, v.Y, v.Z));
                return positions;
            case BSTriShape triShape:
                return triShape.VertexCount > 0 && (triShape.HasVertices || triShape.IsSkinned)
                    ? triShape.VertexPositions
                    : null;
            default:
                return shape.GeometryData?.Vertices;
        }
    }

    /// <summary>
    /// The shape's own triangle list (BSTriShape family, NiTriShape), or for legacy NiTriStrips the
    /// strips of its NiTriStripsData converted to triangles (alternating winding, degenerate
    /// triangles and strips shorter than 3 points skipped).
    /// </summary>
    public static List<Triangle>? GetTriangles(INiShape shape)
    {
        try
        {
            if (shape.Triangles is { Count: > 0 } triangles) return triangles;
        }
        catch (NotImplementedException)
        {
            return null; // BSGeometry (Starfield) does not implement Triangles.
        }

        return shape.GeometryData is NiTriStripsData data ? GetStripTriangles(data) : null;
    }

    /// <summary>
    /// NiTriStripsData has no public strip API (NiflySharp's generator skips it), so the protected
    /// <c>_points</c> (flat index list, sum of <c>_stripLengths</c> entries) and <c>_stripLengths</c>
    /// are read by reflection.
    /// </summary>
    private static List<Triangle>? GetStripTriangles(NiTriStripsData data)
    {
        if (StripPointsField == null || StripLengthsField == null)
        {
            ReportStripFieldsMissingOnce();
            return null;
        }
        try
        {
            if (StripPointsField.GetValue(data) is not List<ushort> { Count: > 0 } points
                || StripLengthsField.GetValue(data) is not List<ushort> { Count: > 0 } stripLengths)
                return null;

            var strips = points.SplitByFlexSize(stripLengths).ToList();
            return strips.Count > 0 ? IndicesHelper.GenerateTrianglesFromStrips(strips) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            return null; // Strip lengths that do not match the point list.
        }
    }

    private static void ReportStripFieldsMissingOnce()
    {
        if (Interlocked.Exchange(ref _stripFieldsMissingReported, 1) != 0) return;
        Console.WriteLine(
            "Warning: NiTriStripsData._points/_stripLengths not found in this NiflySharp version; "
            + "NiTriStrips shapes are used as points in the touch test.");
    }

    /// <summary>Editor markers have no dedicated API; they are recognised by the Creation Kit naming convention.</summary>
    public static bool IsEditorMarker(string? name) =>
        name != null && name.Contains("EditorMarker", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the NIF "hidden" bit (0x1) of NiAVObject flags. NiflySharp splits the Flags field by
/// Bethesda stream version (nif.xml: uint when BSVER > 26, ushort otherwise; LE is 83, SSE 100);
/// only the field matching the file's stream version is populated.
/// </summary>
internal readonly record struct AvObjectFlags(bool UsesUIntFlags)
{
    private const int LastBsVersionWithUShortFlags = 26;
    private const uint HiddenBit = 0x1;

    public static AvObjectFlags For(NifFile nif) => new(nif.Header.Version?.StreamVersion > LastBsVersionWithUShortFlags);

    /// <summary>
    /// Objects with a time controller never count as hidden: animated NIFs (furniture, carts,
    /// doors, ...) often store parts with the hidden bit set and show them through a visibility
    /// controller at runtime.
    /// </summary>
    public bool IsHiddenWithoutController(uint flagsUi, ushort flagsUs, NiBlockRef<NiTimeController>? controller) =>
        ((UsesUIntFlags ? flagsUi : flagsUs) & HiddenBit) != 0
        && (controller == null || controller.IsEmpty());
}
