namespace AddedObjectRemover;

/// <summary>The settings of a batch cannot be used; thrown so that Synthesis marks the run failed and writes no output.</summary>
public sealed class BatchStoppedException(string message) : InvalidOperationException(message);
