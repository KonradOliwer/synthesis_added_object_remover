namespace AddedObjectRemover;

/// <param name="Geometry">Set only on success.</param>
/// <param name="Error">Why the read did not succeed; null on success.</param>
/// <param name="ErrorKind">Short category of <paramref name="Error"/> for the run statistics.</param>
/// <param name="Warning">Non-fatal problem of a successful read.</param>
internal sealed record NifReadResult(
    MeshReadStatus Status,
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
    public const string EffectOnlyKind = "effect-only mesh";

    /// <summary>Set when the file footer's root node was used instead of NiflySharp's GetRootNode().</summary>
    public NifRoot? FooterRoot { get; init; }

    /// <summary>Set when a NiTriStrips shape needed strip fields this NiflySharp version lacks.</summary>
    public bool StripFieldsMissing { get; init; }

    public static NifReadResult Succeeded(NifGeometry geometry, string? warning) =>
        new(MeshReadStatus.Success, geometry, null, null, warning);

    public static NifReadResult Failed(string kind, string error) =>
        new(MeshReadStatus.Failed, null, error, kind, null);

    public static NifReadResult WithoutRenderGeometry(string error) =>
        new(MeshReadStatus.NoRenderGeometry, null, error, NoRenderGeometryKind, null);

    public static NifReadResult WithEffectsOnly(string error) =>
        new(MeshReadStatus.EffectOnly, null, error, EffectOnlyKind, null);
}
