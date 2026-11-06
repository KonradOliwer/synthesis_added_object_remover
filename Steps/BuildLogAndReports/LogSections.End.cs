using System.Collections.Immutable;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    public static LogSection BuildWriteSection(WriteSummary written, TimeSpan elapsed, ReportContext context)
    {
        var overrides = written.Overrides;
        List<string> lines =
        [
            $"Wrote {TextFormat.Count(overrides.Values.Sum())} overrides ({TextFormat.Count(overrides.GetValueOrDefault(WrittenPart.Removed))} removed"
                + string.Concat(NamedParts(overrides, UntitledParts).Select(part => Describe.ListSeparator + part))
                + string.Concat(TitledParts.Select(group => TitledPartsText(overrides, group.Title, group.Parts)))
                + ")"
                + CopiesAdded(written)
                + $"{Describe.TimedSuffix(elapsed, context)}.",
        ];
        if (written.EnableParentsReplaced > 0)
        {
            lines.Add(
                $"  {TextFormat.Count(written.EnableParentsReplaced)} removed objects had an Enable Parent; it was replaced by the player "
                + "with \"opposite of parent\" so they stay disabled.");
        }
        return new LogSection("write", [.. lines]);
    }

    /// <summary>A part of the line that reports the written patch: what was done to its records, after a count of 1 and after any other count.</summary>
    private readonly record struct WriteLinePart(WrittenPart Part, string Singular, string Plural)
    {
        public WriteLinePart(WrittenPart part, string what)
            : this(part, what, what)
        {
        }
    }

    /// <summary>The parts after the removed ones, before the titled groups, in the order printed.</summary>
    private static readonly ImmutableArray<WriteLinePart> UntitledParts =
    [
        new(WrittenPart.Restored, "restored"),
        new(WrittenPart.KeptMarkerMoved, "kept markers moved"),
        new(WrittenPart.Fitted, "shrunk or turned to fit"),
        new(WrittenPart.WhereRemoved, "to restore where the cleaned mod was removed"),
    ];

    /// <summary>The titled groups of parts, in the order printed.</summary>
    private static readonly ImmutableArray<(string Title, ImmutableArray<WriteLinePart> Parts)> TitledParts =
    [
        ("other mods' objects", [new(WrittenPart.OtherModsObjectPulledDown, "pulled down"), new(WrittenPart.OtherModsObjectRemoved, "removed")]),
        (
            "follow-up of what went back",
            [
                new(WrittenPart.FollowUpRemoved, "removed"),
                new(WrittenPart.FollowUpRestored, "restored"),
                new(WrittenPart.FollowUpOtherModsObjectPulledDown, "other mods' object pulled down", "other mods' objects pulled down"),
                new(WrittenPart.FollowUpOtherModsObjectRemoved, "other mods' object removed", "other mods' objects removed"),
            ]),
    ];

    /// <summary>"2 restored" for each of the parts that has any record, in their order.</summary>
    private static IEnumerable<string> NamedParts(ImmutableDictionary<WrittenPart, int> overrides, ImmutableArray<WriteLinePart> parts) =>
        parts.Where(part => overrides.GetValueOrDefault(part.Part) > 0).Select(part => TextFormat.Count(overrides[part.Part], part.Singular, part.Plural));

    /// <summary>"; title: 2 pulled down, 1 removed", leaving out a part without any record; nothing when none has any.</summary>
    private static string TitledPartsText(ImmutableDictionary<WrittenPart, int> overrides, string title, ImmutableArray<WriteLinePart> parts)
    {
        List<string> named = [.. NamedParts(overrides, parts)];
        return named.Count > 0 ? $"; {title}: {string.Join(Describe.ListSeparator, named)}" : string.Empty;
    }

    /// <summary>" and added 2 new records (copies of other mods' objects that went back, at the cleaned mod's place)", or nothing when there are none.</summary>
    private static string CopiesAdded(WriteSummary written) =>
        written.Clones.IsEmpty
            ? string.Empty
            : $" and added {TextFormat.Count(written.Clones.Length, "new record", "new records")} (copies of other mods' objects that went back, at the cleaned mod's place)";

    public static LogSection BuildDoneSection(TimeSpan elapsed, ReportContext context) =>
        new("done", [$"Done{Describe.TimedSuffix(elapsed, context)}."]);
}
