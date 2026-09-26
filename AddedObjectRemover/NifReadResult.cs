namespace AddedObjectRemover;

internal enum NifReadStatus
{
    Success,

    /// <summary>The NIF could not be found, parsed, or holds invalid data (e.g. out-of-range coordinates).</summary>
    Failed,

    /// <summary>The NIF parsed fine but has no visible render geometry (e.g. only editor-marker shapes).</summary>
    NoRenderGeometry,
}

/// <param name="Geometry">Set only on success.</param>
/// <param name="Error">Why the read did not succeed; null on success.</param>
/// <param name="ErrorKind">Short category of <paramref name="Error"/> for the run statistics.</param>
/// <param name="Warning">Non-fatal problem of a successful read.</param>
internal sealed record NifReadResult(
    NifReadStatus Status,
    NifGeometry? Geometry,
    string? Error,
    string? ErrorKind,
    string? Warning)
{
    public const string NotFoundKind = "not found";
    public const string LoadFailedKind = "not loadable";
    public const string NoRootNodeKind = "no root node";
    public const string InvalidCoordinatesKind = "invalid vertex coordinates";
    public const string NoRenderGeometryKind = "no visible render geometry";

    public static NifReadResult Succeeded(NifGeometry geometry, string? warning) =>
        new(NifReadStatus.Success, geometry, null, null, warning);

    public static NifReadResult Failed(string kind, string error) =>
        new(NifReadStatus.Failed, null, error, kind, null);

    public static NifReadResult WithoutRenderGeometry(string error) =>
        new(NifReadStatus.NoRenderGeometry, null, error, NoRenderGeometryKind, null);
}
