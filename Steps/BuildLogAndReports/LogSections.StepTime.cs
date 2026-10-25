using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    public const string MeshReadingStepName = "Preparing to read meshes";
    public const string ModsStepName = "Identifying the mods";
    public const string NonPlacedLinksStepName = "Reading the links from records other than placed objects";
    public const string ObjectsToKeepStepName = "Finding the objects to keep";
    private const string FitStepName = "Shrinking or turning clashing objects";

    /// <summary>The section with the detailed log's step-time line at its end.</summary>
    /// <remarks>Only for a step whose section has no line that carries the step's time.</remarks>
    public static LogSection WithStepTime(LogSection section, string stepName, TimeSpan elapsed, ReportContext context) =>
        section with { Lines = [.. section.Lines, .. StepTimeLines(stepName, elapsed, context)] };

    private static ImmutableArray<string> StepTimeLines(string stepName, TimeSpan elapsed, ReportContext context) =>
        context.Detailed ? [$"Step time: {stepName} in {TextFormat.Seconds(elapsed)}."] : [];
}
