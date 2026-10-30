namespace AddedObjectRemover.Tests.Fixtures;

internal static class TestVisibleLeftBehind
{
    /// <summary>A switched-off step, with the also-remove follow-up switched off too.</summary>
    public static VisibleLeftBehindResult SwitchedOff { get; } = new(StepOutcome.SwitchedOff, [], [], [], TimeSpan.Zero, TestRestingObjects.SwitchedOff, Failure: null);
}
