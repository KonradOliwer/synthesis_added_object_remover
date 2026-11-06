using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>Report words every part key the modules record failures under; a key without wording is a bug.</summary>
public class RunPartTextTests
{
    private const string Failure = "InvalidOperationException: broke";

    private static readonly ReportContext Normal = new(Bases: null!, Shapes: null!, Detailed: false);
    private static readonly ReportContext Detailed = new(Bases: null!, Shapes: null!, Detailed: true);
    private static readonly FailureSubject Object = new("000801:Other.esp", "Crate", Position: 1);

    public static TheoryData<string, string> WholeParts { get; } = new()
    {
        { "ReportFiles", "  Unexpected error in the report files (InvalidOperationException: broke). Some or all of the report files are missing; the patch is not affected." },
        { "AlsoRemove", "  Unexpected error in the also-remove step (InvalidOperationException: broke). The also-remove rounds did not complete, so objects resting on removed ones were not removed." },
        { "FollowUp", "  Unexpected error in the also-remove follow-ups of the objects left behind (InvalidOperationException: broke). The follow-ups stopped there: the removals of the passes before stand, and objects resting on what the later rounds removed were not removed." },
        { "TouchChains", "  Unexpected error in the touch-chain search (InvalidOperationException: broke). The chains of touching objects that the detailed log and the report files list are missing." },
        { "LeftBehind", "  Unexpected error in the step for invisible objects left behind (InvalidOperationException: broke). That step did not run, so invisible objects whose surroundings were removed were not removed." },
        { "MarkerMoves", "  Unexpected error in the marker moves (InvalidOperationException: broke). No kept marker was moved, so markers inside other mods' objects stay where they are." },
        { "ReportFileDetails", "  Unexpected error in the report-file details (InvalidOperationException: broke). The touching pairs or mesh origins that the report files list are missing from them." },
        { "Step", "  Unexpected error in the step that restores what the cleaned mod changed where it was removed (InvalidOperationException: broke). Nothing was restored by area; the objects restored because they themselves were removed are written as decided." },
    };

    public static TheoryData<string, string> PerSubjectParts { get; } = new()
    {
        { "TooCloseCheck", "  Unexpected error while checking objects for being too close (InvalidOperationException: broke): 1 object, e.g. Crate [000801:Other.esp]. They were not removed." },
        { "TerrainCheck", "  Unexpected error while checking objects for changed terrain (InvalidOperationException: broke): 1 object, e.g. Crate [000801:Other.esp]. They were not removed." },
        { "KeepCheck", "  Unexpected error while finding the cleaned mod's objects to keep (InvalidOperationException: broke): 1 object, e.g. Crate [000801:Other.esp]. They were kept." },
        { "ObjectCheck", "  Unexpected error while checking objects the cleaned mod changed against what stays (InvalidOperationException: broke): 1 object, e.g. Crate [000801:Other.esp]. They stay as the cleaned mod changed them." },
        { "ObjectThatStays", "  Unexpected error while measuring objects that stay, which objects restored where the cleaned mod was removed are checked against (InvalidOperationException: broke): 1 object, e.g. Crate [000801:Other.esp]. They were treated as invisible, so an object restored next to them was not checked against them for coming too close." },
        { "RemovalCauseSelection", "  Unexpected error while selecting the objects that can cause removals (InvalidOperationException: broke): 1 object, e.g. Crate [000801:Other.esp]. They were not used to remove the cleaned mod's objects." },
    };

    [Theory]
    [MemberData(nameof(WholeParts))]
    public void AWholePartFailureIsWordedAsToday(string part, string line)
    {
        Assert.Equal(line, Assert.Single(LinesOf(new PartFailure(KeyNamed(part), Failure, Subject: null))));
    }

    [Theory]
    [MemberData(nameof(PerSubjectParts))]
    public void AFailureForOneObjectIsWordedAsToday(string part, string line)
    {
        Assert.Equal(line, Assert.Single(LinesOf(new PartFailure(KeyNamed(part), Failure, Object))));
    }

    [Fact]
    public void AKeyWithoutWordingThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RunPartText.Word(new PartFailure(new PartKey("Elsewhere", "Unknown"), Failure, Subject: null)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RunPartText.LinkedStep((RoundKind)99));
    }

    [Theory]
    [InlineData(RoundKind.ChangedTerrain, "changed-terrain")]
    [InlineData(RoundKind.TooClose, "too-close")]
    [InlineData(RoundKind.AlsoRemove, "follow-up")]
    [InlineData(RoundKind.VisibleLeftBehind, "visible left-behind")]
    [InlineData(RoundKind.InvisibleLeftBehind, "left-behind")]
    public void TheLinkedGroupsLabelNamesTheStepOfTheRoundKind(RoundKind kind, string step)
    {
        Assert.Equal(step, RunPartText.LinkedStep(kind));
        Assert.Equal(
            $"Linked groups: 3 more objects removed with the {step} removals they are linked to.",
            Assert.Single(LogSections.BuildLinkedRemovalLines(3, 0, 0, RunPartText.LinkedStep(kind), Detailed)));
    }

    [Fact]
    public void AFailureWithoutExceptionThrows()
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
            ReportFileParts.ReportFiles, RestingObjectsParts.AlsoRemove, RestingObjectsParts.FollowUp, RestingObjectsParts.TouchChains, InvisibleLeftBehindParts.LeftBehind, MarkerMoveParts.MarkerMoves,
            RestingObjectsParts.ReportFileDetails, ChangedTerrainParts.TerrainCheck, TooCloseParts.TooCloseCheck, KeepParts.KeepCheck, SelectionParts.RemovalCauseSelection,
            RestoreWhereRemovedParts.Step, RestoreWhereRemovedParts.ObjectCheck, RestoreWhereRemovedParts.ObjectThatStays, RestoreWhereRemovedParts.StandingTerrainCheck,
        }.Single(key => key.Name == name);
}
