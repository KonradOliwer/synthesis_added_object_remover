namespace AddedObjectRemover;

internal static partial class LogSections
{
    /// <param name="kept">The objects the leftover round held.</param>
    /// <remarks>The kept lines come first, then the problems the phase met, then the per-object decisions and the summary.</remarks>
    public static LogSection Leftovers(
        World world,
        LeftoverResult leftovers,
        IEnumerable<KeptTarget> kept,
        PhaseProblems problems,
        TimeSpan elapsed,
        ReportContext context)
    {
        List<string> lines = [.. Kept(world, kept, context), .. Problems(problems, context)];
        if (context.Detailed) lines.AddRange(leftovers.Evaluations.Select(evaluation => LeftoverDecisionLine(world, evaluation)));
        lines.Add(LeftoverSummaryLine(leftovers, elapsed, context));
        return new LogSection("leftovers", [.. lines]);
    }

    public static LogSection Relocations(
        World world,
        RelocationResult relocations,
        RelocationOptions options,
        PhaseProblems problems,
        ReportContext context)
    {
        List<string> lines = [.. Problems(problems, context)];
        foreach (var move in relocations.Moved)
        {
            var target = world.Targets[move.Evaluation.TargetIndex];
            if (context.Detailed)
            {
                lines.Add(
                    $"  Moved kept {RecordNames.Describe(target)} in {world.SpaceNames[target.SpaceKey]} "
                    + $"out of {Describe.OtherObject(move.Evaluation.ContainingObject!.Value)}: {move.Distance:F0} units onto the {move.Surface.ToString().ToLowerInvariant()}.");
            }
            if (move.LeftHomeCell)
            {
                lines.Add(
                    $"  Warning: {RecordNames.Describe(target)} was moved into the neighboring cell "
                    + $"({ExteriorGrid.CellIndex(move.To.X)}, {ExteriorGrid.CellIndex(move.To.Y)}) because its own cell has no free spot.");
            }
        }
        foreach (var evaluation in relocations.LeftInPlace)
        {
            var target = world.Targets[evaluation.TargetIndex];
            lines.Add(
                $"  Left kept {RecordNames.Describe(target)} in {world.SpaceNames[target.SpaceKey]} "
                + $"inside {Describe.OtherObject(evaluation.ContainingObject!.Value)}: no free navmesh or terrain spot within {options.MaxDistance:F0} units.");
        }
        lines.Add($"Moved {relocations.Moved.Count:N0} kept markers out of other mods' objects; {relocations.LeftInPlace.Count:N0} left in place.");
        return new LogSection("relocations", [.. lines]);
    }

    private static string LeftoverDecisionLine(World world, LeftoverEvaluation evaluation)
    {
        var target = world.Targets[evaluation.TargetIndex];
        return $"  Leftover {RecordNames.Describe(target)} ({evaluation.Kind}) in {world.SpaceNames[target.SpaceKey]}: "
            + $"{(evaluation.IsRemoved ? "removed" : "kept")}, {Describe.LeftoverReason(evaluation)}; radius {evaluation.Radius:F0}, "
            + $"removed/total ground area {evaluation.Surroundings.Describe()}.";
    }

    private static string LeftoverSummaryLine(LeftoverResult leftovers, TimeSpan elapsed, ReportContext context)
    {
        var decisions = Enum.GetValues<LeftoverDecision>();
        return $"Leftover invisible objects: {leftovers.Evaluations.Count:N0} evaluated{Describe.TimedSuffix(elapsed, context)}; "
            + $"removed {leftovers.RemovedCount:N0} ({CountDecisions(leftovers, decisions.Where(decision => decision.IsRemoval()))}); "
            + $"kept {leftovers.Evaluations.Count - leftovers.RemovedCount:N0} "
            + $"({CountDecisions(leftovers, decisions.Where(decision => !decision.IsRemoval()))}).";
    }

    private static string CountDecisions(LeftoverResult leftovers, IEnumerable<LeftoverDecision> decisions) =>
        string.Join(", ", decisions.Select(decision => $"{leftovers.CountDecisions(decision):N0} {decision.Describe()}"));
}
