using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>Why reading a mesh gave no usable geometry.</summary>
public enum MeshReadError { NotFound, NotLoadable, NoRootNode, InvalidCoordinates, NoRenderGeometry, EffectOnly, Malformed, UnexpectedException }

/// <summary>The first vertex that made a mesh's coordinates invalid.</summary>
/// <param name="NonFinite">The vertex was not finite; otherwise it was beyond the limit.</param>
/// <param name="Limit">The largest accepted absolute coordinate.</param>
public readonly record struct CoordinateFault(bool NonFinite, float Limit);

/// <summary>How the shapes of one mesh were counted or skipped.</summary>
/// <param name="Unsupported">Shapes of block types that are not render geometry, by type name (ordinal order), with their counts.</param>
/// <param name="HiddenIgnored">The hidden flag was ignored in a second pass.</param>
public sealed record ShapeCounts(
    int Shapes,
    int Counted,
    int Hidden,
    int EditorMarker,
    int EffectShader,
    ImmutableArray<KeyValuePair<string, int>> Unsupported,
    int NoVertices,
    int Unreachable,
    int HiddenAncestor,
    int MarkerAncestor,
    int MismatchedStrips,
    bool HiddenIgnored);

/// <summary>Why one mesh read failed, as facts only; each fact is set only for the kinds it belongs to.</summary>
/// <param name="LoadCode">The library's load result; only for <see cref="MeshReadError.NotLoadable"/>.</param>
/// <param name="Coordinates">Only for <see cref="MeshReadError.InvalidCoordinates"/>.</param>
/// <param name="Shapes">Only for <see cref="MeshReadError.NoRenderGeometry"/> and <see cref="MeshReadError.EffectOnly"/>.</param>
/// <param name="ExceptionType">The library exception's type name; only for <see cref="MeshReadError.Malformed"/>.</param>
/// <param name="ExceptionMessage">The library exception's message; only for <see cref="MeshReadError.Malformed"/>.</param>
public sealed record MeshReadFailure(
    MeshReadError Kind,
    int? LoadCode,
    CoordinateFault? Coordinates,
    ShapeCounts? Shapes,
    string? ExceptionType,
    string? ExceptionMessage)
{
    public static MeshReadFailure NotFound() => Without(MeshReadError.NotFound);

    public static MeshReadFailure NotLoadable(int loadCode) => new(MeshReadError.NotLoadable, loadCode, null, null, null, null);

    public static MeshReadFailure NoRootNode() => Without(MeshReadError.NoRootNode);

    public static MeshReadFailure InvalidCoordinates(CoordinateFault fault) => new(MeshReadError.InvalidCoordinates, null, fault, null, null, null);

    public static MeshReadFailure NoRenderGeometry(ShapeCounts shapes) => new(MeshReadError.NoRenderGeometry, null, null, shapes, null, null);

    public static MeshReadFailure EffectOnly(ShapeCounts shapes) => new(MeshReadError.EffectOnly, null, null, shapes, null, null);

    public static MeshReadFailure Malformed(string exceptionType, string exceptionMessage) =>
        new(MeshReadError.Malformed, null, null, null, exceptionType, exceptionMessage);

    private static MeshReadFailure Without(MeshReadError kind) => new(kind, null, null, null, null, null);
}

/// <summary>A mesh failure as the run statistics count it: its kind, and for a malformed file the library exception's type.</summary>
public readonly record struct MeshFailureCategory(MeshReadError Kind, string? ExceptionType);
