using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>Report words every part key the modules record failures under; a key without wording is a bug.</summary>
public class RunPartTextTests
{
    private const string Failure = "InvalidOperationException: broke";

    private static readonly ReportContext Normal = new(Bases: null!, Shapes: null!, Detailed: false);
    private static readonly ReportContext Detailed = new(Bases: null!, Shapes: null!, Detailed: true);
    private static readonly FailureSubject Object = new("000801:Other.esp", "Crate", Position: 1);
    private static readonly FailureSubject Mesh = new(@"meshes\crate.nif", Name: null, Position: 0);

    public static TheoryData<string, string> WholeParts { get; } = new()
    {
        { "ReportFiles", "  Unexpected error in the report files (InvalidOperationException: broke). Some or all of the report files are missing; the patch is not affected." },
        { "AlsoRemove", "  Unexpected error in the also-remove step (InvalidOperationException: broke). The also-remove rounds did not complete, so objects resting on removed ones were not removed." },
        { "TouchChains", "  Unexpected error in the touch-chain search (InvalidOperationException: broke). The chains of touching objects that the detailed log and the report files list are missing." },
        { "LeftBehind", "  Unexpected error in the left-behind step (InvalidOperationException: broke). The left-behind step did not run, so invisible objects whose surroundings were removed were not removed." },
        { "MarkerMoves", "  Unexpected error in the marker moves (InvalidOperationException: broke). No kept marker was moved, so markers inside other mods' objects stay where they are." },
        { "ReportFileDetails", "  Unexpected error in the report-file details (InvalidOperationException: broke). The touching pairs or mesh origins that the report files list are missing from them." },
        { "LoaderStartUp", "  Unexpected error in the NIF loader start-up (InvalidOperationException: broke). Meshes are read one at a time, which is slower; the results are not affected." },
    };

    public static TheoryData<string, string> PerSubjectParts { get; } = new()
    {
        { "TooCloseCheck", "  Unexpected error while checking objects for being too close (InvalidOperationException: broke): 1 object, e.g. Crate [000801:Other.esp]. They were not removed." },
        { "KeepCheck", "  Unexpected error while finding the target objects to keep (InvalidOperationException: broke): 1 object, e.g. Crate [000801:Other.esp]. They were kept." },
        { "RemovalCauseSelection", "  Unexpected error while selecting the objects that can cause removals (InvalidOperationException: broke): 1 object, e.g. Crate [000801:Other.esp]. They were not used to remove target objects." },
        { "MeshShapes", @"  Unexpected error while reading mesh shapes (InvalidOperationException: broke): 1 mesh, e.g. meshes\crate.nif. Objects using the mesh are treated as having no mesh shape." },
        { "MeshTriangles", @"  Unexpected error while reading mesh triangles (InvalidOperationException: broke): 1 mesh, e.g. meshes\crate.nif. Objects using the mesh are treated as having no mesh triangles." },
    };

    [Theory]
    [MemberData(nameof(WholeParts))]
    public void AWholePartFailureIsWordedAsToday(string part, string line)
    {
        Assert.Equal(line, Assert.Single(LinesOf(new PartFailure(KeyNamed(part), Failure, Subject: null))));
    }

    [Theory]
    [MemberData(nameof(PerSubjectParts))]
    public void AFailureForOneObjectOrMeshIsWordedAsToday(string part, string line)
    {
        var key = KeyNamed(part);
        var subject = key.Owner == MeshReadingParts.MeshShapes.Owner ? Mesh : Object;

        Assert.Equal(line, Assert.Single(LinesOf(new PartFailure(key, Failure, subject))));
    }

    [Fact]
    public void AKeyWithoutWordingThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RunPartText.Word(new PartFailure(new PartKey("Elsewhere", "Unknown"), Failure, Subject: null)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RunPartText.LinkedStep((RoundKind)99));
    }

    [Theory]
    [InlineData(RoundKind.TooClose, "too-close")]
    [InlineData(RoundKind.AlsoRemove, "follow-up")]
    [InlineData(RoundKind.LeftBehind, "leftover invisible object")]
    public void TheLinkedGroupsLabelNamesTheStepOfTheRoundKind(RoundKind kind, string step)
    {
        Assert.Equal(step, RunPartText.LinkedStep(kind));
        Assert.Equal(
            $"Linked groups: 3 more objects removed with the {step} removals they are linked to.",
            Assert.Single(LogSections.BuildLinkedRemovalLines(3, RunPartText.LinkedStep(kind), Detailed)));
    }

    [Fact]
    public void ALoaderStartUpWhoseReloadedFileHeldNoBlocksIsWordedAsToday()
    {
        Assert.Equal(
            "  Unexpected error in the NIF loader start-up (the minimal NIF it saved and loaded again held no blocks). "
            + "Meshes are read one at a time, which is slower; the results are not affected.",
            Assert.Single(LinesOf(new PartFailure(MeshReadingParts.LoaderStartUp, Failure: null, Subject: null))));
    }

    [Fact]
    public void AFailureWithoutExceptionOfAKeyWithoutSuchWordingThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RunPartText.Word(new PartFailure(RestingObjectsParts.AlsoRemove, Failure: null, Subject: null)));
    }

    [Fact]
    public void ASubjectGivenToAWholePartIsABug()
    {
        Assert.Throws<UnreachableException>(() => RunPartText.Word(new PartFailure(RestingObjectsParts.AlsoRemove, Failure, Object)));
    }

    [Fact]
    public void ANoSubjectGivenToAPerSubjectPartIsABug()
    {
        Assert.Throws<UnreachableException>(() => RunPartText.Word(new PartFailure(KeepParts.KeepCheck, Failure, Subject: null)));
    }

    private static IEnumerable<string> LinesOf(PartFailure failure) => UnexpectedErrorLines.Of([RunPartText.Word(failure)], Normal);

    private static PartKey KeyNamed(string name) =>
        new[]
        {
            ReportFileParts.ReportFiles, RestingObjectsParts.AlsoRemove, RestingObjectsParts.TouchChains, LeftBehindParts.LeftBehind, MarkerMoveParts.MarkerMoves,
            RestingObjectsParts.ReportFileDetails, TooCloseParts.TooCloseCheck, KeepParts.KeepCheck, SelectionParts.RemovalCauseSelection,
            MeshReadingParts.MeshShapes, MeshReadingParts.MeshTriangles, MeshReadingParts.LoaderStartUp,
        }.Single(key => key.Name == name);
}
