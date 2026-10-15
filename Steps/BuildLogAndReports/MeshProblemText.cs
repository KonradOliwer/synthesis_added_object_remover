using System.Diagnostics;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The log lines of the problems the mesh tools record as facts.</summary>
internal static class MeshProblemText
{
    public static string Line(AssetProblem problem) => problem.Kind switch
    {
        AssetProblemKind.NotFound => $"  [mesh] not found: {problem.Mesh}",
        AssetProblemKind.Unreadable => $"  [mesh] unreadable: {problem.Mesh} ({ReadFailure(Required(problem.ReadFailure))})",
        AssetProblemKind.TrianglesUnreadable => $"  [mesh] triangles unreadable: {problem.Mesh} ({ReadFailure(Required(problem.ReadFailure))})",
        AssetProblemKind.ReadWarning =>
            $"  [mesh] {problem.Mesh}: {Required(problem.MismatchedStrips)} NiTriStrips shape(s) whose strip lengths do not match their points are used as points.",
        AssetProblemKind.LooseFileUnreadable => $"  [mesh] could not read loose file {problem.LoosePath}: {problem.Detail}",
        AssetProblemKind.ArchiveExtractFailed =>
            $"  [mesh] could not extract {problem.Mesh} from archive: {problem.ExceptionType}: {problem.Detail}",
        AssetProblemKind.FooterRoot => FooterRootLine(problem),
        _ => throw new UnreachableException($"Unknown mesh problem kind {problem.Kind}."),
    };

    public static string Line(ArchiveProblem problem) => problem.Kind switch
    {
        ArchiveProblemKind.ArchiveUnreadable => $"  Warning: could not read archive {problem.Subject}: {problem.ExceptionMessage}",
        ArchiveProblemKind.DataFolderUnlistable => $"  Warning: could not list archives in {problem.Subject}: {problem.ExceptionMessage}",
        ArchiveProblemKind.IniArchiveListUnreadable => $"  Warning: could not read archive list from game INI: {problem.ExceptionMessage}",
        ArchiveProblemKind.StripFieldsMissing =>
            "Warning: NiTriStripsData._points/_stripLengths not found in this NiflySharp version; "
            + "NiTriStrips shapes are used as points in the touch test.",
        _ => throw new UnreachableException($"Unknown archive problem kind {problem.Kind}."),
    };

    /// <summary>Why a mesh read failed, as the mesh problem lines put it in brackets.</summary>
    public static string ReadFailure(MeshReadFailure failure) => failure.Kind switch
    {
        MeshReadError.NotFound => "mesh file not found",
        MeshReadError.NotLoadable => $"NifFile.Load failed with code {failure.LoadCode}.",
        MeshReadError.NoRootNode => "NIF has no root NiNode.",
        MeshReadError.InvalidCoordinates => InvalidCoordinatesText(Required(failure.Coordinates)),
        MeshReadError.EffectOnly => $"NIF contains only effect-shader render geometry ({ShapeCountsText(Required(failure.Shapes))}).",
        MeshReadError.NoRenderGeometry => $"NIF contains no visible render geometry ({ShapeCountsText(Required(failure.Shapes))}).",
        MeshReadError.Malformed => $"{failure.ExceptionType} while reading NIF: {failure.ExceptionMessage}",
        _ => throw new UnreachableException($"A mesh read failure of kind {failure.Kind} is not recorded as a mesh problem."),
    };

    /// <summary>A mesh failure as the "Mesh failures" line counts it.</summary>
    public static string FailureCategory(MeshFailureCategory category) => category.Kind switch
    {
        MeshReadError.NotFound => "not found",
        MeshReadError.NotLoadable => "not loadable",
        MeshReadError.NoRootNode => "no root node",
        MeshReadError.InvalidCoordinates => "invalid vertex coordinates",
        MeshReadError.NoRenderGeometry => "no visible render geometry",
        MeshReadError.Malformed => Required(category.ExceptionType),
        MeshReadError.UnexpectedException => "unexpected error",
        _ => throw new UnreachableException($"A mesh read of kind {category.Kind} is not counted as a mesh failure."),
    };

    private static string InvalidCoordinatesText(CoordinateFault fault) =>
        fault.NonFinite ? "NIF has non-finite vertex coordinates." : $"NIF has vertex coordinates beyond ±{fault.Limit:0} units.";

    private static string ShapeCountsText(ShapeCounts shapes)
    {
        var unsupported = shapes.Unsupported.Length == 0
            ? "0"
            : $"{shapes.Unsupported.Sum(entry => entry.Value)} [{string.Join(", ", shapes.Unsupported.Select(entry => $"{entry.Key} x{entry.Value}"))}]";
        return $"{shapes.Shapes} shapes: counted={shapes.Counted}, hidden={shapes.Hidden}, editorMarker={shapes.EditorMarker}, effectShader={shapes.EffectShader}, "
            + $"unsupported={unsupported}, noVertices={shapes.NoVertices}, unreachable={shapes.Unreachable}, "
            + $"hiddenAncestor={shapes.HiddenAncestor}, markerAncestor={shapes.MarkerAncestor}, mismatchedStrips={shapes.MismatchedStrips}"
            + (shapes.HiddenIgnored ? ", hidden flag ignored in 2nd pass" : string.Empty);
    }

    private static T Required<T>(T? fact) where T : class =>
        fact ?? throw new UnreachableException($"The mesh problem lacks its {typeof(T).Name} fact.");

    private static T Required<T>(T? fact) where T : struct =>
        fact ?? throw new UnreachableException($"The mesh problem lacks its {typeof(T).Name} fact.");

    private static string FooterRootLine(AssetProblem problem)
    {
        var blocks = Required(problem.FooterRoot);
        return $"  [mesh] {problem.Mesh}: footer root block {blocks.Used} used instead of first-node root block {blocks.FirstNodeRoot}";
    }
}
