using System.Collections.Immutable;

namespace AddedObjectRemover.Run.RunSettingsValidation.Contracts;

/// <summary>One run's settings with where they came from.</summary>
public sealed record ConfiguredSettings(SettingsSource Source, ISettingsValues Settings);

/// <summary>The settings of every run in order, or the one problem that stops them all.</summary>
public abstract record ConfigsRead
{
    /// <summary>The end of every failure message: a failed batch stops before any config runs.</summary>
    public const string NothingWasRun = " Nothing was run and no patch was written.";

    public sealed record Ready(ImmutableArray<ConfiguredSettings> Configs) : ConfigsRead;

    public sealed record Failed(string Message) : ConfigsRead;
}
