using System.Collections.Immutable;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    public static LogSection Write(WriteSummary written, TimeSpan elapsed, ReportContext context)
    {
        List<string> lines =
        [
            $"Wrote {TextFormat.Count(written.Removed + written.Moved)} overrides ({TextFormat.Count(written.Removed)} removed, {TextFormat.Count(written.Moved)} moved){Describe.TimedSuffix(elapsed, context)}.",
        ];
        if (written.EnableParentsReplaced > 0)
        {
            lines.Add(
                $"  {TextFormat.Count(written.EnableParentsReplaced)} removed objects had an Enable Parent; it was replaced by the player "
                + "with \"opposite of parent\" so they stay disabled.");
        }
        return new LogSection("write", [.. lines]);
    }

    public static LogSection Done(TimeSpan elapsed, ReportContext context) =>
        new("done", [$"Done{Describe.TimedSuffix(elapsed, context)}."]);
}
