using System.Collections.Immutable;

namespace AddedObjectRemover.Run.RunSettingsValidation.Contracts;

/// <summary>Every run of the batch in order, or the one problem that stops them all before any runs.</summary>
public abstract record BatchRequest
{
    public sealed record Ready(ImmutableArray<RunRequest> Requests) : BatchRequest;

    public sealed record Failed(string Message) : BatchRequest;
}
