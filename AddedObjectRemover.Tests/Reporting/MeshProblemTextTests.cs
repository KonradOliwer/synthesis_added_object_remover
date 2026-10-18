using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>Report words the facts the mesh tools record, and orders the mesh problems itself.</summary>
public class MeshProblemTextTests
{
    private const string Mesh = @"meshes\a.nif";

    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);

    public static TheoryData<AssetProblem, string> MeshProblems { get; } = new()
    {
        { AssetProblem.NotFound(Mesh), @"  [mesh] not found: meshes\a.nif" },
        { AssetProblem.Unreadable(Mesh, MeshReadFailure.NoRootNode()), @"  [mesh] unreadable: meshes\a.nif (NIF has no root NiNode.)" },
        {
            AssetProblem.TrianglesUnreadable(Mesh, MeshReadFailure.NotFound()),
            @"  [mesh] triangles unreadable: meshes\a.nif (mesh file not found)"
        },
        {
            AssetProblem.TrianglesUnreadableAfterGoodRead(Mesh, MeshReadFailure.NotFound()),
            @"  [mesh] triangles unreadable after reading fine earlier in the run: meshes\a.nif (mesh file not found)"
        },
        {
            AssetProblem.TrianglesUnreadableAfterGoodRead(Mesh, "IOException: broke"),
            @"  [mesh] triangles unreadable after reading fine earlier in the run: meshes\a.nif (IOException: broke)"
        },
        {
            AssetProblem.Unreadable(Mesh, MeshReadFailure.NoRenderGeometry(Shapes([], hiddenIgnored: false))),
            @"  [mesh] no visible geometry (normal for markers and dummy items): meshes\a.nif (20 shapes: counted=1, hidden=2, editorMarker=3, effectShader=4, "
            + "unsupported=0, noVertices=5, unreachable=6, hiddenAncestor=7, markerAncestor=8, mismatchedStrips=9)"
        },
        {
            AssetProblem.TrianglesUnreadable(Mesh, MeshReadFailure.NoRenderGeometry(Shapes([], hiddenIgnored: false))),
            @"  [mesh] no visible geometry (normal for markers and dummy items): meshes\a.nif (20 shapes: counted=1, hidden=2, editorMarker=3, effectShader=4, "
            + "unsupported=0, noVertices=5, unreachable=6, hiddenAncestor=7, markerAncestor=8, mismatchedStrips=9)"
        },
        {
            AssetProblem.ReadWarning(Mesh, 2),
            @"  [mesh] meshes\a.nif: 2 NiTriStrips shape(s) whose strip lengths do not match their points are used as points."
        },
        {
            AssetProblem.LooseFileUnreadable(Mesh, @"C:\Data\meshes\a.nif", "denied"),
            @"  [mesh] could not read loose file C:\Data\meshes\a.nif: denied"
        },
        {
            AssetProblem.ArchiveExtractFailed(Mesh, "InvalidDataException", "corrupt"),
            @"  [mesh] could not extract meshes\a.nif from archive: InvalidDataException: corrupt"
        },
        {
            AssetProblem.FooterRootUsed(Mesh, new FooterRootBlocks(7, 2)),
            @"  [mesh] meshes\a.nif: footer root block 7 used instead of first-node root block 2"
        },
    };

    public static TheoryData<ArchiveProblem, string> ArchiveProblems { get; } = new()
    {
        { new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "corrupt"), "  Warning: could not read archive a.bsa: corrupt" },
        { new ArchiveProblem(ArchiveProblemKind.DataFolderUnlistable, "/data", "denied"), "  Warning: could not list archives in /data: denied" },
        {
            new ArchiveProblem(ArchiveProblemKind.IniArchiveListUnreadable, "game INI", "locked"),
            "  Warning: could not read archive list from game INI: locked"
        },
        {
            new ArchiveProblem(ArchiveProblemKind.StripFieldsMissing, "NiTriStripsData", ExceptionMessage: null),
            "Warning: NiTriStripsData._points/_stripLengths not found in this NiflySharp version; NiTriStrips shapes are used as points in the touch test."
        },
    };

    public static TheoryData<MeshReadFailure, string> ReadFailures { get; } = new()
    {
        { MeshReadFailure.NotFound(), "mesh file not found" },
        { MeshReadFailure.NotLoadable(3), "NifFile.Load failed with code 3." },
        { MeshReadFailure.NoRootNode(), "NIF has no root NiNode." },
        { MeshReadFailure.InvalidCoordinates(new CoordinateFault(NonFinite: true, 1e6f)), "NIF has non-finite vertex coordinates." },
        { MeshReadFailure.InvalidCoordinates(new CoordinateFault(NonFinite: false, 1e6f)), "NIF has vertex coordinates beyond ±1000000 units." },
        {
            MeshReadFailure.EffectOnly(Shapes([], hiddenIgnored: false)),
            "NIF contains only effect-shader render geometry (20 shapes: counted=1, hidden=2, editorMarker=3, effectShader=4, unsupported=0, "
            + "noVertices=5, unreachable=6, hiddenAncestor=7, markerAncestor=8, mismatchedStrips=9)."
        },
        {
            MeshReadFailure.NoRenderGeometry(Shapes([new("BSLODTriShape", 1), new("NiParticles", 2)], hiddenIgnored: true)),
            "NIF contains no visible render geometry (20 shapes: counted=1, hidden=2, editorMarker=3, effectShader=4, "
            + "unsupported=3 [BSLODTriShape x1, NiParticles x2], noVertices=5, unreachable=6, hiddenAncestor=7, markerAncestor=8, "
            + "mismatchedStrips=9, hidden flag ignored in 2nd pass)."
        },
        {
            MeshReadFailure.Malformed("EndOfStreamException", "Unable to read beyond the end of the stream."),
            "EndOfStreamException while reading NIF: Unable to read beyond the end of the stream."
        },
    };

    public static TheoryData<MeshFailureCategory, string> FailureCategories { get; } = new()
    {
        { new MeshFailureCategory(MeshReadError.NotFound, null), "not found" },
        { new MeshFailureCategory(MeshReadError.NotLoadable, null), "not loadable" },
        { new MeshFailureCategory(MeshReadError.NoRootNode, null), "no root node" },
        { new MeshFailureCategory(MeshReadError.InvalidCoordinates, null), "invalid vertex coordinates" },
        { new MeshFailureCategory(MeshReadError.NoRenderGeometry, null), "no visible geometry (normal for markers and dummy items)" },
        { new MeshFailureCategory(MeshReadError.Malformed, "EndOfStreamException"), "EndOfStreamException" },
        { new MeshFailureCategory(MeshReadError.UnexpectedException, null), "unexpected error" },
    };

    [Theory]
    [MemberData(nameof(ReadFailures))]
    public void AReadFailureGivesTheTextOfItsKind(MeshReadFailure failure, string expected) =>
        Assert.Equal(expected, MeshProblemText.ReadFailure(failure));

    [Theory]
    [MemberData(nameof(FailureCategories))]
    public void AFailureCategoryGivesTheWordsOfItsKind(MeshFailureCategory category, string expected) =>
        Assert.Equal(expected, MeshProblemText.FailureCategory(category));

    [Theory]
    [MemberData(nameof(MeshProblems))]
    public void AMeshProblemGivesTheLineOfItsKind(AssetProblem problem, string expected) =>
        Assert.Equal(expected, MeshProblemText.Line(problem));

    [Theory]
    [MemberData(nameof(ArchiveProblems))]
    public void AnArchiveProblemGivesTheLineOfItsKind(ArchiveProblem problem, string expected) =>
        Assert.Equal(expected, MeshProblemText.Line(problem));

    [Fact]
    public void TheMeshProblemsOfOneMeshPrintInOneOrderWhicheverOrderTheyWereRecorded()
    {
        var notFound = AssetProblem.NotFound(Mesh);
        var warning = AssetProblem.ReadWarning(Mesh, 1);
        var other = AssetProblem.NotFound(@"meshes\B.nif");

        var forward = LogSections.BuildMeshProblemsSection([notFound, warning, other], [], Detailed).Lines.ToArray();
        var backward = LogSections.BuildMeshProblemsSection([other, warning, notFound], [], Detailed).Lines.ToArray();

        Assert.Equal(forward, backward);
        Assert.Equal(
            [
                @"  [mesh] meshes\a.nif: 1 NiTriStrips shape(s) whose strip lengths do not match their points are used as points.",
                @"  [mesh] not found: meshes\a.nif",
                @"  [mesh] not found: meshes\B.nif",
            ],
            forward);
    }

    private static ShapeCounts Shapes(ImmutableArray<KeyValuePair<string, int>> unsupported, bool hiddenIgnored) =>
        new(20, 1, 2, 3, 4, unsupported, 5, 6, 7, 8, 9, hiddenIgnored);
}
