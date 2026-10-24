using System.Collections.Immutable;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    public static LogSection BuildWriteSection(WriteSummary written, TimeSpan elapsed, ReportContext context)
    {
        var total = written.Removed.Length + written.Moved + written.Fitted + written.OtherModsObjectsRemoved + written.OtherModsObjectsPulledDown;
        List<string> lines =
        [
            $"Wrote {TextFormat.Count(total)} overrides ({TextFormat.Count(written.Removed.Length)} removed, {TextFormat.Count(written.Moved)} moved"
                + WhenAny(written.Fitted, "shrunk or turned to fit")
                + OtherModsObjectsWritten(written)
                + $"){Describe.TimedSuffix(elapsed, context)}.",
        ];
        if (written.EnableParentsReplaced > 0)
        {
            lines.Add(
                $"  {TextFormat.Count(written.EnableParentsReplaced)} removed objects had an Enable Parent; it was replaced by the player "
                + "with \"opposite of parent\" so they stay disabled.");
        }
        return new LogSection("write", [.. lines]);
    }

    /// <summary>", 3 what", or nothing when there are none.</summary>
    private static string WhenAny(int count, string what) => count > 0 ? $", {TextFormat.Count(count)} {what}" : string.Empty;

    /// <summary>"; other mods' objects: 2 pulled down, 1 removed", leaving out a count of 0; nothing when both are 0.</summary>
    private static string OtherModsObjectsWritten(WriteSummary written)
    {
        List<string> parts =
        [
            .. new[] { (Count: written.OtherModsObjectsPulledDown, What: "pulled down"), (Count: written.OtherModsObjectsRemoved, What: "removed") }
                .Where(part => part.Count > 0)
                .Select(part => $"{TextFormat.Count(part.Count)} {part.What}"),
        ];
        return parts.Count > 0 ? $"; other mods' objects: {string.Join(Describe.ListSeparator, parts)}" : string.Empty;
    }

    public static LogSection BuildDoneSection(TimeSpan elapsed, ReportContext context) =>
        new("done", [$"Done{Describe.TimedSuffix(elapsed, context)}."]);
}
