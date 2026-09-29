using System.Collections.Immutable;

namespace AddedObjectRemover;

internal static partial class LogSections
{
    public static LogSection Write(WriteSummary written, TimeSpan elapsed, ReportContext context)
    {
        List<string> lines =
        [
            $"Wrote {written.Removed + written.Moved:N0} overrides ({written.Removed:N0} removed, {written.Moved:N0} moved){Describe.TimedSuffix(elapsed, context)}.",
        ];
        if (written.EnableParentsReplaced > 0)
        {
            lines.Add(
                $"  {written.EnableParentsReplaced:N0} removed objects had an Enable Parent; it was replaced by the player "
                + "with \"opposite of parent\" so they stay disabled.");
        }
        return new LogSection("write", [.. lines]);
    }

    public static LogSection Done(TimeSpan elapsed, ReportContext context) =>
        new("done", [$"Done{Describe.TimedSuffix(elapsed, context)}."]);
}
