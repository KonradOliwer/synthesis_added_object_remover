namespace AddedObjectRemover.Tests.Fixtures;

internal static class TestWholeGroups
{
    /// <summary>A switched-off step for whole groups, with its also-remove follow-up switched off too.</summary>
    public static WholeGroupsResult SwitchedOff { get; } = new(StepOutcome.SwitchedOff, [], [], [], TestRestingObjects.SwitchedOff, Failure: null);
}
