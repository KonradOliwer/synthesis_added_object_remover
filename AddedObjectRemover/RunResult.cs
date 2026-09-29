namespace AddedObjectRemover;

/// <summary>How a run ended.</summary>
internal abstract record RunResult
{
    /// <summary>The run made no changes.</summary>
    public sealed record Stopped(StopReason Reason) : RunResult;

    /// <param name="Explanations">The evidence behind the report files; none when no files are written.</param>
    public sealed record Done(Outcome Outcome, Explanations Explanations) : RunResult;
}
