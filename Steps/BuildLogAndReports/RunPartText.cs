using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The words the log uses for the parts of the run that can fail unexpectedly, by the part keys their modules own.</summary>
internal static class RunPartText
{
    /// <param name="Part">What failed, as a phrase.</param>
    /// <param name="Consequence">What is missing or incomplete in the results when the part failed.</param>
    /// <param name="Subjects">What the part's failures happen for; null for a part that fails as a whole.</param>
    private sealed record PartWording(string Part, string Consequence, SubjectKind? Subjects);

    private static readonly IReadOnlyDictionary<PartKey, PartWording> Wordings = new Dictionary<PartKey, PartWording>
    {
        [ReportFileParts.ReportFiles] = new("the report files", "Some or all of the report files are missing; the patch is not affected.", Subjects: null),
        [RestingObjectsParts.AlsoRemove] = new("the also-remove step", "The also-remove rounds did not complete, so objects resting on removed ones were not removed.", Subjects: null),
        [RestingObjectsParts.TouchChains] = new("the touch-chain search", "The chains of touching objects that the detailed log and the report files list are missing.", Subjects: null),
        [LeftBehindParts.LeftBehind] = new("the step for invisible objects left behind", "That step did not run, so invisible objects whose surroundings were removed were not removed.", Subjects: null),
        [MarkerMoveParts.MarkerMoves] = new("the marker moves", "No kept marker was moved, so markers inside other mods' objects stay where they are.", Subjects: null),
        [RestingObjectsParts.ReportFileDetails] = new("the report-file details", "The touching pairs or mesh origins that the report files list are missing from them.", Subjects: null),
        [FitParts.FitCheck] = new("checking objects shrunk or turned", "Those sizes and turns were not used.", SubjectKind.Object),
        [FitParts.RestingCheck] = new("finding the objects other objects rest on", "No object was shrunk or turned; the objects that were too close were removed.", Subjects: null),
        [ChangedTerrainParts.TerrainCheck] = new("checking objects for changed terrain", "They were not removed.", SubjectKind.Object),
        [TooCloseParts.TooCloseCheck] = new("checking objects for being too close", "They were not removed.", SubjectKind.Object),
        [KeepParts.KeepCheck] = new("finding the cleaned mod's objects to keep", "They were kept.", SubjectKind.Object),
        [SelectionParts.RemovalCauseSelection] = new("selecting the objects that can cause removals", "They were not used to remove the cleaned mod's objects.", SubjectKind.Object),
        [MeshReadingParts.MeshShapes] = new("reading mesh shapes", "Objects using the mesh are treated as having no mesh shape.", SubjectKind.Mesh),
        [MeshReadingParts.MeshTriangles] = new("reading mesh triangles", "Objects using the mesh are treated as having no mesh triangles.", SubjectKind.Mesh),
        [MeshReadingParts.LoaderStartUp] = new("the NIF loader start-up", "Meshes are read one at a time, which is slower; the results are not affected.", Subjects: null),
    };

    /// <summary>How the log names a part that failed without an exception.</summary>
    private static readonly IReadOnlyDictionary<PartKey, string> FailuresWithoutException = new Dictionary<PartKey, string>
    {
        [MeshReadingParts.LoaderStartUp] = "the minimal NIF it saved and loaded again held no blocks",
    };

    /// <summary>The failure in the log's words: its part's phrase and consequence, and its subject's label.</summary>
    public static UnexpectedError Word(PartFailure failure)
    {
        var wording = Wordings.GetValueOrDefault(failure.Part) ?? throw Unknown(failure.Part);
        return new UnexpectedError(
            wording.Part,
            failure.Failure ?? FailuresWithoutException.GetValueOrDefault(failure.Part) ?? throw Unknown(failure.Part),
            wording.Consequence,
            SubjectOf(failure.Subject, wording)) { Details = failure.Details };
    }

    /// <summary>How the "Linked groups" line names the step whose removals the linked ones followed.</summary>
    public static string LinkedStep(RoundKind kind) =>
        kind switch
        {
            RoundKind.ChangedTerrain => "changed-terrain",
            RoundKind.TooClose => "too-close",
            RoundKind.AlsoRemove => "follow-up",
            RoundKind.LeftBehind => "left-behind",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The round kind has no linked-groups wording."),
        };

    private static ErrorSubject? SubjectOf(FailureSubject? subject, PartWording wording) =>
        (subject, wording.Subjects) switch
        {
            (null, null) => null,
            ({ } given, { } kind) => new ErrorSubject(kind, RecordLabels.Of(given.Key, given.Name), given.Position),
            (null, not null) => throw new UnreachableException($"The part {wording.Part} fails for a subject but was given none."),
            (not null, null) => throw new UnreachableException($"The part {wording.Part} fails as a whole but was given a subject."),
        };

    private static ArgumentOutOfRangeException Unknown(PartKey part) =>
        new(nameof(part), part, "The run part has no wording for this use.");
}
