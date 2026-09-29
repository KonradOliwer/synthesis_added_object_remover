using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>The report files of a run, as tables of fields already formatted as CSV text.</summary>
internal static partial class Tables
{
    public const string EdgesFileName = "edges.csv";
    public const string ComponentsFileName = "components.csv";
    public const string AnchoringFileName = "anchoring.csv";
    public const string MeshOriginsFileName = "mesh-origins.csv";
    public const string LeftoversFileName = "leftover-invisible-objects.csv";
    public const string HintsFileName = "manual-patch-hints.csv";

    /// <param name="explanations">Decides which explanation files exist.</param>
    /// <param name="leftovers">Null when the leftover invisible objects step is off.</param>
    /// <returns>In the order the steps ran; none when report files are off.</returns>
    public static ImmutableArray<CsvTable> Build(
        Outcome outcome,
        Explanations explanations,
        ReportOptions reports,
        LeftoverOptions? leftovers,
        FollowUpOptions followUp,
        ReportContext context)
    {
        if (!reports.WriteFiles) return [];

        var tables = ImmutableArray.CreateBuilder<CsvTable>();
        if (explanations.Touch is { } touch) tables.AddRange(TouchTables(outcome, touch, followUp.TouchGap, context));
        if (followUp.Mode == FollowUpRemovalMode.ObjectsSupportedByIt && outcome.FollowUp.HadSeeds)
            tables.Add(Anchoring(outcome.World, AnchoringRows.Join(outcome.FollowUp), followUp.SupportLostFraction, context));
        if (explanations.MeshOrigins is { } origins) tables.Add(MeshOrigins(origins));
        if (leftovers != null) tables.Add(Leftovers(outcome.World, outcome.Leftovers.Evaluations, outcome.Relocation, context));
        tables.Add(Hints(outcome.World, outcome.Hints));
        return tables.ToImmutable();
    }

    private static ImmutableArray<string> Row(IEnumerable<string> fields) => [.. fields];
}
