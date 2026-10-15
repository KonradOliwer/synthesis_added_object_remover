namespace AddedObjectRemover;

/// <summary>Whether a step that can be switched off did its work. "Performed, nothing to do" is <see cref="Performed"/> with no proposals.</summary>
public enum StepOutcome
{
    Performed,
    SwitchedOff,
    StoppedByUnexpectedError,

    /// <summary>Not performed, because an earlier part it needs was not performed; that part has its own error.</summary>
    StoppedByEarlierFailure,
}
