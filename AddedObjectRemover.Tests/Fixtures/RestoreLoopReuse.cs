namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>
/// Plans the restore step as it runs, and proves that the work its loop takes over from the pass before changes nothing: the same
/// plan is made once more with every pass recomputing everything, and both must end in the same orders, refusals and their reasons,
/// protected points, kept water levels and number of passes.
/// </summary>
internal static class RestoreLoopReuse
{
    /// <param name="input">The step's input; the reference run reads it again, so nothing in it may count or record what it is asked.</param>
    /// <param name="referenceInput">The same input built afresh, for the reference run to read; none to read <paramref name="input"/> again.</param>
    /// <returns>The step's result with the loop as it runs.</returns>
    public static RestoreWhereRemovedResult Plan(WhereRemovedRestorer.Input input, WhereRemovedRestorer.Input? referenceInput = null)
    {
        var result = WhereRemovedRestorer.Plan(input);
        if (result.Outcome == StepOutcome.Performed) Assert.Equal(Describe(result), Describe(PlanRecomputingEveryPass(referenceInput ?? input)));
        return result;
    }

    private static RestoreWhereRemovedResult PlanRecomputingEveryPass(WhereRemovedRestorer.Input input)
    {
        var inputs = new WhereRemovedRestorer.StepInputs(input, input.Settings);
        var plans = WhereRemovedRestorer.BuildPlans(inputs);
        var (placedGuards, terrainGuards, loopInputs) = WhereRemovedRestorer.BuildGuards(inputs, plans);
        var (state, passes) = new FullRecomputeRestoreLoop(placedGuards, terrainGuards, loopInputs).Run(plans.Start);
        return WhereRemovedRestorer.ResultOf(plans, state, passes, input.Settings);
    }

    private static string Describe(RestoreWhereRemovedResult result) => string.Join(
        Environment.NewLine,
        result.Objects.Select(judged => $"{judged.Change.Key} {judged.Refused} {judged.IsRestored} {judged.Change.Earlier.Undo}")
            .Concat(result.Cells.Select(cell => $"{cell.Change.Cell} {string.Join(",", cell.Undone.Select(part => part.Name))} {cell.WaterKeptBy} {cell.IsRestored}"))
            .Concat(result.Lands.Select(land =>
                $"{land.Change.Cell} {string.Join(",", land.Heights.Points)} {string.Join(";", land.Kept.Select(kept => $"{kept.Reason}:{string.Join(",", kept.Points.Points)}"))} {string.Join(",", land.FinalHeights ?? [])}"))
            .Concat(result.NavmeshGroups.Select(group => $"{string.Join(",", group.Members)} {group.Refused} {NavmeshWriteDescription.Of(group.Write)}"))
            .Concat(result.DoorsLeftRemoved.Select(door => $"door left removed {door}"))
            .Concat(result.Failures.Select(failure => $"{failure.Part} {failure.Failure} {failure.Subject?.Key} {failure.Subject?.Position}"))
            .Append($"{result.Passes}"));
}
